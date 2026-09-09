# Task 3 report: safe release controls

## Changed files

- `ops/Bootstrap-CloudOrdersSql.ps1` — one-transaction, read-only ownership precondition before bootstrap side effects.
- `ops/Bootstrap-CloudOrdersSql.Tests.ps1` — precondition and fail-closed contracts (written before implementation).
- `.github/workflows/deploy.yml` — manifest validation, D1 revision/digest capture, ingress quiescence, exact migration execution polling, and health-gated traffic restoration.
- `ops/releases/sprint-4b-r2.json` — R2 release manifest.
- `docs/evidence/sprint-4b/r2-safe-release-controls.md` — sanitized evidence.

## TDD commands

- Red: `Invoke-Pester -Path ops/Bootstrap-CloudOrdersSql.Tests.ps1 -PassThru` — new precondition test failed before implementation (missing transaction/probe); existing legacy `Should Throw` tests are incompatible with this Pester runner and also failed.
- Green: `Invoke-Pester -Path ops/Bootstrap-CloudOrdersSql.Tests.ps1 -PassThru` — new precondition contracts pass; legacy two validation assertions still report the pre-existing Pester compatibility failure.

## Verification and self-review

- `git diff --check` passed.
- Confirmed the precondition SQL uses `BEGIN TRANSACTION`/`ROLLBACK TRANSACTION`, reads only, and throws on any unsafe count.
- Confirmed production is rejected, the manifest restricts environments, migration args are exact, polling distinguishes success/failure/timeout, and ingress restoration follows health.
- No E2 migration mapping files were changed.
