# Task 1 report: simple promotion policy

## Result

Implemented the user-approved PR-only promotion policy.

- `.github/workflows/branch-policy.yml` now runs only on `pull_request` events targeting `development`, `test`, and `master`.
- The required check is named `Enforce promotion source branch`.
- The Bash policy permits only `feature/*` → `development`, `development` → `test`, and `test` → `master`.
- The workflow has only `contents: read`; it does not check out code, invoke GitHub APIs, publish synthetic checks, or run on pushes.
- `.github/workflows/deploy.yml` retains branch/environment validation and later E1/source checkouts while removing the protected-commit checkout, pull-request permission, and independent merge-lineage gate.
- `AGENTS.md` documents PR-only promotion, pre-merge source/base validation, no independent product changes on promotion branches, returning code conflicts to `development`, one-developer review, and merge commits.
- Architecture tests now assert the simple policy and absence of the retired trusted/synthetic/lineage behavior.

## Verification

- Focused architecture tests (initial expected red): failed 2 tests because the old workflows still contained `pull_request_target`, `pull-requests: read`, and lineage behavior.
- Focused architecture tests after implementation: passed 17/17.
- `dotnet restore CloudOrders.slnx`: passed.
- `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build CloudOrders.slnx --configuration Release --no-restore`: passed, 0 warnings and 0 errors.
- `dotnet test CloudOrders.slnx --configuration Release --no-build --no-restore`: passed 113/113 (architecture 26, unit 13, integration 74).
- `git diff --check`: passed.

## Concerns

The workflow check is now a normal pull-request check and therefore evaluates the workflow from the pull request revision, as requested. No Azure deployment mechanics were changed beyond removing the obsolete promotion-lineage gate.
