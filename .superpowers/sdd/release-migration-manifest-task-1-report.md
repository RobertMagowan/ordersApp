# Task 1 report: release migration descriptor

## Implementation

- Added `ops/releases/release-schema.json`, a strict JSON Schema draft 2020-12 contract with `additionalProperties: false`, all descriptor fields required, constrained environment and policy enums, and unique EF migration ID arrays.
- Added `ops/releases/current-release.json` with schema version 1, API deployment enabled, development/test-only scope, ownership read-only precondition, controlled non-production traffic, API-compatible mode, and the complete four-migration EF baseline through `20260909213051_AddOutboxLeasing`.
- Extended `DeploymentWorkflowPolicyTests` to read the descriptor and schema and exercise valid behavior plus malformed JSON, duplicate IDs, production scope, unknown policy values, and non-cumulative migration authorization.

## TDD record

1. Added the required descriptor contract test before creating either release document.
2. Ran the focused architecture test and observed the expected `FileNotFoundException` because `current-release.json` was absent.
3. Added the descriptor and schema, then added the remaining contract tests and semantic checks.
4. Fixed analyzer issues in the test implementation and reran focused and complete architecture tests successfully.

## Self-review

- Descriptor keys exactly match the schema required set; no runner or deployment workflow files were changed.
- Baseline and authorization arrays are chronological, unique, full EF IDs; authorization is constrained to a cumulative baseline prefix.
- Production is excluded both by the descriptor and by the test contract; policy values are closed enums.
- `git diff --check` passed.

## Verification

- Focused: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --filter FullyQualifiedName~DeploymentWorkflowPolicyTests` — 24 passed.
- Complete: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release` — 45 passed.

## Review follow-up

- Strengthened the architecture tests to inspect every schema property definition (types, enum values, migration references, regex, and uniqueness) and validate descriptor values against those definitions.
- Separated generic schema validity from first-release assertions and added a valid code-only descriptor case (`deployApi: false`, no authorized migrations, maintenance-required compatibility).
- Added semantic rejection coverage for reversed migration arrays as well as non-prefix authorization; ordering and cumulative-prefix checks are enforced by the test validator because JSON Schema cannot express array ordering.
- Follow-up focused: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --filter FullyQualifiedName~DeploymentWorkflowPolicyTests` — 26 passed.
- Follow-up complete: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release` — 47 passed.

## Re-review follow-up

- Corrected the code-only validity case to retain the complete cumulative authorization and baseline.
- Added a separate test proving `deployApi: false` is independently accepted by the schema contract.
- Focused: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --filter FullyQualifiedName~DeploymentWorkflowPolicyTests` — 27 passed.
