# Task 1 report: deployment path scope classifier

## Implementation

Added `ops/Get-DeploymentScope.ps1`, exposing `Get-DeploymentScope -ChangedPaths <string[]> -ComparisonAvailable <bool>` and returning `deployable` plus `reason`. The classifier fails closed when comparison is unavailable, recognizes deployment-relevant source, test, infrastructure, local, release, workflow, helper, Docker, solution, and build-input paths, and treats delivery documentation/state paths as delivery-only. Unknown paths are deployable with reason `unknown_path`. When run directly, the script accepts standard-input JSON and emits compact JSON.

Added focused Pester 4 coverage in `ops/tests/SprintDelivery.Tests.ps1` for delivery-only, deployable, mixed/fail-closed, unavailable-comparison, and unknown-path behavior.

## TDD evidence

RED command (before creating the helper):

```text
powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1
```

Result: exit code 1; existing 63 tests passed and the new `Deployment path scope` describe block failed in `BeforeAll` because `ops/Get-DeploymentScope.ps1` did not exist.

GREEN command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1
```

Result: exit code 0; `Passed: 67 Failed: 0 Skipped: 0 Pending: 0 Inconclusive: 0`.

Also verified direct standard-input JSON invocation produces `{"deployable":false,"reason":"delivery_only"}` for a documentation-only change, and `git diff --check` is clean.

## Self-review and concerns

- The classifier deliberately gives unknown paths deployable status to preserve fail-closed behavior.
- Path matching normalizes backslashes and one leading `./`; callers should provide repository-relative paths.
- No workflow YAML, CI, Azure, identity, or production files were modified. Task 2 still needs to connect this helper to `deploy.yml`.

