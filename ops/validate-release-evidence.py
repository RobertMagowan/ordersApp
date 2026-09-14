"""Validate execution-scoped runner logs and emit only allowlisted SQL-history evidence."""
import hashlib
import json
from pathlib import Path
import sys


def validate(descriptor_path, expected_sha, mode, require_complete, log_lines):
    descriptor_bytes = Path(descriptor_path).read_bytes()
    if hashlib.sha256(descriptor_bytes).hexdigest() != expected_sha:
        raise ValueError("Descriptor identity mismatch")
    descriptor = json.loads(descriptor_bytes)
    baseline = descriptor["requiredMigrationBaseline"]
    records = []
    for line in log_lines:
        try:
            record = json.loads(line)
            if isinstance(record, dict) and isinstance(record.get("Log"), str):
                record = json.loads(record["Log"])
        except (ValueError, TypeError):
            continue
        if isinstance(record, dict) and "descriptorSha256" in record:
            records.append(record)
    if not records:
        raise ValueError("No runner evidence")
    for record in records:
        applied = record.get("applied")
        outstanding = record.get("outstanding")
        if (record.get("releaseId") != descriptor["releaseId"]
                or record.get("descriptorSha256") != expected_sha
                or record.get("mode") != mode
                or record.get("baseline") != baseline
                or not isinstance(applied, list)
                or len(applied) > len(baseline)
                or applied != baseline[:len(applied)]
                or outstanding != baseline[len(applied):]
                or any(item not in descriptor["authorisedMigrations"] for item in outstanding)):
            raise ValueError("Runner identity or baseline mismatch")
        if require_complete and (applied != baseline or outstanding != []):
            raise ValueError("Release baseline is incomplete")
    # Never echo raw log fields or unexpected properties (e.g. connection strings).
    return {key: records[-1][key] for key in
            ("releaseId", "descriptorSha256", "mode", "applied", "baseline", "outstanding")}


if __name__ == "__main__":
    try:
        descriptor_path, expected_sha, mode, complete = sys.argv[1:]
        if mode not in {"verify", "apply"} or complete not in {"true", "false"}:
            raise ValueError("Invalid evidence policy")
        print(json.dumps(validate(descriptor_path, expected_sha, mode, complete == "true", sys.stdin), separators=(",", ":")))
    except (ValueError, TypeError, KeyError, OSError):
        raise SystemExit("MIGRATION_BASELINE_CONFLICT: sanitised runner evidence did not confirm the requested release state.")
