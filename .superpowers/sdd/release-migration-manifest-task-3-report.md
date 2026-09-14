# Task 3 report: descriptor-driven migration workflow

## Initial review remediation

Commit `583516c` corrected literal output newlines, embedded the descriptor in the migration image/build context, propagated conditional Bash function failures, required sanitised runner evidence with exact final baseline equality, rejected disallowed nonproduction environments before Azure mutation, bypassed descriptor validation on master, and captured migration-only API invariants before provisioning.

Verification for that commit: 171 .NET tests passed (19 unit, 43 architecture, 109 integration), 11 executable workflow regressions passed, format/build passed with zero warnings/errors, nine YAML jobs parsed, and all 34 Bash scripts passed syntax checks. The migration Docker image built successfully and its descriptor SHA-256 matched the checkout when read as the image's non-root user.

## Final review follow-up

- Reject any nonempty migration container command before starting the Job, retaining the immutable image's entrypoint. The execution command is queried and independently checked before polling success. A JSON object projection preserves an absent command as a queryable null value.
- Retain the pre-start template until execution identity validation completes. Require one unambiguous `ConnectionStrings__CloudOrders` binding, and compare its exact value and secret reference against the execution environment. Changed values/references, value-to-reference substitutions, missing bindings, and duplicate bindings fail closed.
- Connection values and secret references are never included in comparison diagnostics or summaries. Regression fixtures assert that sensitive marker values are absent from standard output and standard error.
- Added executable regressions by invoking the workflow's actual embedded Python blocks and testing the actual conditional Bash function call. Observed failures against the previous implementation before adding the guards.

## Follow-up verification

- `python tests/workflow/test_release_workflow.py`: 14 tests passed, including parameterised command and SQL-binding mutations.
- Focused deployment/workflow architecture tests: 22 passed.
- `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --no-restore`: 43 passed.
- `dotnet format --verify-no-changes --no-restore`: passed.
- `dotnet build --configuration Release --no-restore`: passed, zero warnings and errors.
- YAML parse and `bash -n`: nine jobs and 34 Bash blocks passed.
- `git diff --check`: passed.

No Azure deployment or external mutation was performed. Live development verification remains a release gate owned by the parent task.
