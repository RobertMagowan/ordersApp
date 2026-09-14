"""Execute the actual workflow's embedded scripts without Azure credentials."""
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = (ROOT / ".github/workflows/deploy.yml").read_text(encoding="utf-8")


def step(name):
    match = re.search(r"^      - name: " + re.escape(name) + r"\n(.*?)(?=^      - |^  \w+:|\Z)", WORKFLOW, re.M | re.S)
    return match.group(1)


def script(name):
    return "\n".join(line[10:] for line in step(name).split("run: |\n", 1)[1].splitlines()) + "\n"


def python_block(name):
    return re.search(r"<<'PY'(?: \|\| return 1)?\n(.*?)\nPY", script(name), re.S).group(1)


class ReleaseWorkflowRegressionTests(unittest.TestCase):
    def test_template_rejects_nonempty_entrypoint_override(self):
        body = script("Verify, apply, and verify the release descriptor")
        block = re.findall(r"<<'PY' \|\| return 1\n(.*?)\nPY", body, re.S)[0]
        for command in (["sh", "-c", "exit 0"], ["dotnet", "Other.dll"], "override"):
            with self.subTest(command=command), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "template.json"
                path.write_text(json.dumps({"containers": [{"name": "migrations", "command": command, "env": [
                    {"name": "ConnectionStrings__CloudOrders", "value": "sensitive-connection"}]}]}))
                result = subprocess.run([sys.executable, "-c", block, str(path), "image", "verify", "sha", "release", "test"],
                                        capture_output=True, text=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("command", result.stderr)
                self.assertNotIn("sensitive-connection", result.stdout + result.stderr)

    def test_template_preserves_default_entrypoint_and_sql_binding(self):
        body = script("Verify, apply, and verify the release descriptor")
        block = re.findall(r"<<'PY' \|\| return 1\n(.*?)\nPY", body, re.S)[0]
        for command in (None, []):
            for binding in ({"value": "sensitive-connection"}, {"secretRef": "sensitive-reference"}):
                with self.subTest(command=command, binding=binding), tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / "template.json"
                    sql = {"name": "ConnectionStrings__CloudOrders", **binding}
                    path.write_text(json.dumps({"containers": [{"name": "migrations", "command": command, "env": [sql]}]}))
                    result = subprocess.run([sys.executable, "-c", block, str(path), "image", "verify", "sha", "release", "test"],
                                            capture_output=True, text=True)
                    self.assertEqual(0, result.returncode, result.stderr)
                    self.assertIn(sql, json.loads(path.read_text())["containers"][0]["env"])
                    self.assertNotIn("sensitive-", result.stdout + result.stderr)

    def test_execution_rejects_command_or_sql_binding_changes(self):
        body = script("Verify, apply, and verify the release descriptor")
        self.assertIn("--query '{command:properties.template.containers[0].command}'", body)
        block = re.findall(r"<<'PY' \|\| return 1\n(.*?)\nPY", body, re.S)[1]
        invocation = re.findall(r"python3 - (.*?) <<'PY' \|\| return 1", body)[1]
        bindings = [{"name": "DESCRIPTOR_SHA256", "value": "sha"}, {"name": "RELEASE_SHA", "value": "release"},
                    {"name": "DEPLOYMENT_ENVIRONMENT", "value": "test"}]
        value = {"name": "ConnectionStrings__CloudOrders", "value": "sensitive-original-connection"}
        secret = {"name": "ConnectionStrings__CloudOrders", "secretRef": "sensitive-original-reference"}
        cases = [(value, [value], None, True), (secret, [secret], [], True),
                 (value, [value], ["sh", "-c", "exit 0"], False),
                 (value, [{**value, "value": "sensitive-wrong-connection"}], None, False),
                 (secret, [{**secret, "secretRef": "sensitive-wrong-reference"}], None, False),
                 (value, [secret], None, False), (secret, [value], None, False),
                 (value, [], None, False), (value, [value, value], None, False)]
        for original, actual, command, allowed in cases:
            with self.subTest(original=original, actual=actual, command=command), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "template.json"
                path.write_text(json.dumps({"containers": [{"name": "migrations", "env": [original, *bindings]}]}))
                environment = {"mode": "verify", "EXECUTION_ARGS": json.dumps(["--verify-release", "/workspace/ops/releases/current-release.json"]),
                               "EXECUTION_IMAGE": "image", "MIGRATION_IMAGE": "image", "EXECUTION_ENV": json.dumps([*actual, *bindings]),
                               "DESCRIPTOR_SHA256": "sha", "RELEASE_SHA": "release", "DEPLOYMENT_ENVIRONMENT": "test",
                               "JOB_TEMPLATE": str(path), "EXECUTION_COMMAND": json.dumps({"command": command})}
                arguments = [environment[token.removeprefix("$")] for token in shlex.split(invocation)]
                result = subprocess.run([sys.executable, "-c", block, *arguments], capture_output=True, text=True)
                self.assertEqual(allowed, result.returncode == 0, result.stderr)
                self.assertNotIn("sensitive-", result.stdout + result.stderr)

    def test_outputs_are_distinct_newline_terminated_records(self):
        block = python_block("Validate immutable release descriptor")
        # Execute the output-writing portion; schema validation is covered separately.
        output_code = block[block.index("with open("):]
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            descriptor = json.loads((ROOT / "ops/releases/current-release.json").read_text())
            result = subprocess.run([sys.executable, "-c", "import json, hashlib\nfrom pathlib import Path\n" +
                                     "descriptor=" + repr(descriptor) + "\ndescriptor_bytes=b'{}'\nallowed=True\n" + output_code],
                                    env={**os.environ, "GITHUB_OUTPUT": str(output)}, capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(5, len(output.read_text().splitlines()))

    def test_production_bypasses_descriptor_validation(self):
        self.assertIn("if: github.ref_name != 'master'", step("Validate immutable release descriptor"))

    def test_disallowed_environment_fails_validation(self):
        block = python_block("Validate immutable release descriptor")
        self.assertRegex(block, r'if not allowed:\s+raise SystemExit')
        # The schema is tested by the contract suite; isolate the environment policy.
        block = block.replace("from jsonschema import Draft202012Validator", "class Draft202012Validator:\n    def __init__(self, schema): pass\n    def iter_errors(self, descriptor): return []")
        with tempfile.TemporaryDirectory() as directory:
            descriptor = json.loads((ROOT / "ops/releases/current-release.json").read_text())
            descriptor["environments"] = ["development"]
            descriptor_path = Path(directory) / "descriptor.json"
            descriptor_path.write_text(json.dumps(descriptor))
            result = subprocess.run([sys.executable, "-c", block, str(descriptor_path), str(ROOT / "ops/releases/release-schema.json"), "test"],
                                    env={**os.environ, "GITHUB_OUTPUT": str(Path(directory) / "output")}, capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("does not permit", result.stderr)
            self.assertFalse((Path(directory) / "output").exists())

    def test_image_contains_descriptor_from_build_context(self):
        dockerfile = (ROOT / "src/CloudOrders.Migrations/Dockerfile").read_text()
        ignore = (ROOT / ".dockerignore").read_text()
        self.assertIn("COPY ops/releases/current-release.json /workspace/ops/releases/current-release.json", dockerfile)
        self.assertNotIn("\nops/\n", ignore)
        self.assertIn("!ops/releases/current-release.json", ignore)

    def test_migration_only_snapshot_precedes_any_provisioning(self):
        self.assertIn("api_snapshot:", WORKFLOW)
        snapshot = step("Inspect existing release")
        self.assertIn("api_snapshot=", snapshot)
        self.assertIn("deployApi=false requires an existing API", snapshot)
        self.assertIn('"$DEPLOYMENT_ENVIRONMENT" != production', snapshot)
        self.assertIn("needs.preview_foundation.outputs.api_snapshot", step("Verify, apply, and verify the release descriptor"))

    def test_runner_evidence_is_required_for_final_and_reconciliation_verification(self):
        body = script("Verify, apply, and verify the release descriptor")
        self.assertIn("validate-release-evidence.py", body)
        self.assertGreaterEqual(body.count("run_release_job verify true"), 2)
        self.assertIn("--execution \"$EXECUTION\"", body)
        self.assertIn("Release ID:", body)
        self.assertIn("Immutable migration image:", body)

    def test_identity_failure_cannot_be_swallowed_by_conditional_function_call(self):
        body = script("Verify, apply, and verify the release descriptor")
        function = body[body.index("run_release_job() {"):body.index('\nif [[ "$PRECONDITION"')]
        bash = "C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else shutil.which("bash")
        # A bad template must return failure before any start, even in an OR-list.
        preamble = '''
set -euo pipefail
JOB_NAME=test AZURE_RESOURCE_GROUP=test MIGRATION_IMAGE=image DESCRIPTOR_SHA256=sha RELEASE_SHA=sha DEPLOYMENT_ENVIRONMENT=test
EXECUTION_EVIDENCE=()
python3() { return 1; }
az() {
  case "$*" in
    *"job start"*) echo MUTATION >&2; echo execution ;;
    *"properties.status"*) echo Succeeded ;;
    *) echo '{}' ;;
  esac
}
sleep() { :; }
'''
        result = subprocess.run([bash, "-c", preamble + function + '\nrun_release_job verify || exit 47\n'],
                                capture_output=True, text=True)
        self.assertEqual(47, result.returncode, result.stderr)
        self.assertNotIn("MUTATION", result.stderr)

    def test_evidence_validation_requires_exact_final_baseline(self):
        validator = ROOT / "ops/validate-release-evidence.py"
        self.assertTrue(validator.exists(), "Missing executable runner evidence validator")
        descriptor_path = ROOT / "ops/releases/current-release.json"
        descriptor_bytes = descriptor_path.read_bytes()
        descriptor = json.loads(descriptor_bytes)
        baseline = descriptor["requiredMigrationBaseline"]
        evidence = {"releaseId": descriptor["releaseId"], "descriptorSha256": hashlib.sha256(descriptor_bytes).hexdigest(),
                    "mode": "verify", "baseline": baseline, "applied": baseline, "outstanding": []}
        cases = [(evidence, True), ({**evidence, "applied": baseline[:-1], "outstanding": baseline[-1:]}, False),
                 ({**evidence, "descriptorSha256": "wrong"}, False), ({**evidence, "releaseId": "wrong"}, False),
                 ({**evidence, "mode": "apply"}, False), ({**evidence, "baseline": []}, False)]
        for value, expected in cases:
            with self.subTest(value=value):
                result = subprocess.run([sys.executable, str(validator), str(descriptor_path), evidence["descriptorSha256"], "verify", "true"],
                                        input=json.dumps({"Log": json.dumps(value)}) + "\n", capture_output=True, text=True)
                self.assertEqual(expected, result.returncode == 0, result.stderr)

    def test_initial_verification_accepts_authorised_suffix_and_redacts_other_fields(self):
        descriptor_path = ROOT / "ops/releases/current-release.json"
        descriptor_bytes = descriptor_path.read_bytes()
        descriptor = json.loads(descriptor_bytes)
        evidence = {"releaseId": descriptor["releaseId"], "descriptorSha256": hashlib.sha256(descriptor_bytes).hexdigest(),
                    "mode": "verify", "baseline": descriptor["requiredMigrationBaseline"], "applied": [],
                    "outstanding": descriptor["requiredMigrationBaseline"], "secret": "must-not-leak"}
        result = subprocess.run([sys.executable, str(ROOT / "ops/validate-release-evidence.py"), str(descriptor_path),
                                 evidence["descriptorSha256"], "verify", "false"],
                                input=json.dumps({"Log": json.dumps(evidence)}) + "\n", capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("must-not-leak", result.stdout + result.stderr)

    def test_all_job_failures_propagate_when_called_conditionally(self):
        body = script("Verify, apply, and verify the release descriptor")
        function = body[body.index("run_release_job() {"):body.index('\nif [[ "$PRECONDITION"')]
        bash = "C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else shutil.which("bash")
        for failure in ("template-query", "start", "args", "image", "env", "command", "identity", "status", "evidence"):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as directory:
                preamble = r'''
set -euo pipefail
JOB_NAME=test AZURE_RESOURCE_GROUP=test MIGRATION_IMAGE=image DESCRIPTOR_SHA256=sha RELEASE_SHA=sha DEPLOYMENT_ENVIRONMENT=test
EXECUTION_EVIDENCE=()
python3() {
  if [[ "$1" == - && "$2" == verify && "$FAILURE" == identity ]]; then return 1; fi
  return 0
}
az() {
  case "$*" in
    *"job show"*) [[ "$FAILURE" != template-query ]] || return 1; echo '{}' ;;
    *"job start"*) [[ "$FAILURE" != start ]] || return 1; echo execution ;;
    *"containers[0].args"*) [[ "$FAILURE" != args ]] || return 1; echo '[]' ;;
    *"containers[0].image"*) [[ "$FAILURE" != image ]] || return 1; echo image ;;
    *"containers[0].env"*) [[ "$FAILURE" != env ]] || return 1; echo '[]' ;;
    *"containers[0].command"*) [[ "$FAILURE" != command ]] || return 1; echo null ;;
    *"properties.status"*) [[ "$FAILURE" != status ]] || return 1; echo Succeeded ;;
    *) return 1 ;;
  esac
}
collect_release_evidence() { [[ "$FAILURE" != evidence ]]; }
sleep() { :; }
'''
                result = subprocess.run([bash, "-c", preamble + function + '\nrun_release_job verify || exit 47\n'],
                                        env={**os.environ, "FAILURE": failure, "GITHUB_STEP_SUMMARY": (Path(directory) / "summary").as_posix()},
                                        capture_output=True, text=True)
                self.assertEqual(47, result.returncode, result.stderr)

    def test_timeout_reconciliation_cannot_pass_incomplete_fresh_verification(self):
        body = script("Verify, apply, and verify the release descriptor")
        orchestration = body[body.index('\nif [[ "$PRECONDITION"'):body.index('\nAFTER_REVISION=')]
        bash = "C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else shutil.which("bash")
        with tempfile.TemporaryDirectory() as directory:
            preamble = r'''
set -euo pipefail
PRECONDITION=none JOB_NAME=test AZURE_RESOURCE_GROUP=test EXECUTION=apply-execution
run_release_job() {
  echo "CALL:$*" >&2
  [[ "$1" == apply ]] && return 2
  [[ "${2:-false}" == true ]] && return 1
  return 0
}
az() { echo Running; }
'''
            result = subprocess.run([bash, "-c", preamble + orchestration],
                                    env={**os.environ, "GITHUB_STEP_SUMMARY": (Path(directory) / "summary").as_posix()},
                                    capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(1, result.stderr.count("CALL:apply"))
            self.assertIn("CALL:verify true", result.stderr)


if __name__ == "__main__":
    unittest.main()
