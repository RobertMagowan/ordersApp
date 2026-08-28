# Sprint 4A Task 1 report

## Scope delivered

- Reconciled active contracts and the current Sprint 4A/Sprint 9 roadmap around verified External ID customers, bare `Orders.Read`/`Orders.Write`, `user.admin`, `/me`, `CustomerProfileId`, actor/target idempotency, and explicit 401/403/404 behavior.
- Kept the prior Sprint 2–4 plan explicitly historical/superseded for its obsolete Sprint 4 terminology.
- Added the exact E1 manifest at `ops/releases/sprint-4a-e1-migration-only.json`.
- Added protected-push manifest validation, a migration-only Job path, API revision/image/traffic preservation checks, and skipping of the ordinary artifact/API path.
- Replaced list-based Container Apps Job polling with capture of `az containerapp job start --query name --output tsv`, an empty-result failure, and polling only the returned execution.
- Added architecture regressions for the contract normalization, exact Job polling, and E1 manifest path.

## Files changed

- `.github/workflows/deploy.yml`
- `docs/contracts/v1-contracts.md`
- `docs/contracts/frontend-design.md`
- `docs/contracts/traceability.md`
- `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`
- `docs/superpowers/plans/2026-08-16-sprints-2-4-execution-plan.md`
- `tests/CloudOrders.ArchitectureTests/ContractPackTests.cs`
- `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`
- `ops/releases/sprint-4a-e1-migration-only.json`

## TDD evidence

Red command:

```powershell
dotnet test tests\CloudOrders.ArchitectureTests\CloudOrders.ArchitectureTests.csproj --configuration Release --no-restore
```

Result: failed 3 of 13 as expected: missing exact started-execution capture/polling, missing Sprint 4A contract terms, and missing E1 manifest/workflow handling.

Green command:

```powershell
dotnet test tests\CloudOrders.ArchitectureTests\CloudOrders.ArchitectureTests.csproj --configuration Release --no-restore
```

Result: passed 13 of 13 after implementation.

## Verification

- `dotnet format --verify-no-changes --no-restore`: passed.
- `dotnet build CloudOrders.slnx --configuration Release --no-restore`: passed with 0 warnings and 0 errors.
- `az bicep lint --file infra/main.bicep`: passed (informational Bicep configuration notices only).
- `az bicep build --file infra/main.bicep`: passed.
- `az bicep build-params --file infra/environments/{development,test,production}.bicepparam`: passed.
- `git diff --check`: passed.
- Full `dotnet test CloudOrders.slnx --configuration Release --no-build`: architecture (13) and unit (8) tests passed; integration tests could not start Testcontainers because Docker was unavailable at `npipe://./pipe/docker_engine`. A direct integration run failed 20 of 30 for that same environmental prerequisite, before application assertions.
- `actionlint` was not installed. A local YAML parser was also unavailable (no Python/Ruby/yq/PowerShell YAML module); YAML validity therefore remains for CI/actionlint validation.

## Self-review

- The manifest schema validation compares the parsed JSON to the exact two-property E1 object.
- The migration-only path is restricted to protected `development`/`test` push execution, has no artifact build/API deployment path, and verifies revision, image digest, and traffic are unchanged.
- All list-based execution polling was removed; both normal and E1 migration paths fail if start returns no execution name.
- No Azure resources, tenants, application registrations, profiles, or bearer authentication implementation were changed.

## Concerns

- Docker must be started/configured before the full Testcontainers integration suite can be considered green.
- Run `actionlint .github/workflows/deploy.yml` in CI or an environment where actionlint is installed; no local YAML parser was available for an additional syntax check.

## Review remediation (Sprint 4A Task 1)

### Findings fixed

- The active v1 contract now defines the concrete actor/target `CustomerProfileId` idempotency request hash inputs, the three-part durable key, and exact E1 legacy compatibility behavior. The obsolete active subject-only primary-key statement is removed and covered by a negative regression assertion.
- The E1 workflow passes the manifest-produced `AddCustomerProfileOwnershipExpand` value as the migration runner's sole `--migration` argument, inspects the started execution's arguments, and rejects an execution that did not receive that exact value. The runner accepts only that argument shape, migrates to the named EF migration rather than all pending migrations, and verifies it was applied.
- Every ordinary deployment job now explicitly excludes `migration_only == 'true'`; the E1 job is only selected by the protected `push` predicate for `development` and `test`.
- Architecture tests now require the exact two-property manifest schema, protected-push predicate, normal-path exclusion, named migration argument/started-execution verification, runner target consumption, and named-migration application verification.

### TDD evidence

Red command:

```powershell
dotnet test tests\CloudOrders.ArchitectureTests\CloudOrders.ArchitectureTests.csproj --configuration Release --no-restore
```

Result: failed 2 newly added regressions as expected (the contract lacked concrete actor/target hash/key/E1 wording, and the workflow did not consume or verify the named manifest migration).

Green command:

```powershell
dotnet test tests\CloudOrders.ArchitectureTests\CloudOrders.ArchitectureTests.csproj --configuration Release --no-restore
```

Result: passed 14 of 14.

### Additional verification

- `dotnet build src\CloudOrders.Migrations\CloudOrders.Migrations.csproj --configuration Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- `npx.cmd --yes prettier@3.6.2 --check .github/workflows/deploy.yml`: YAML parsed successfully, but reported pre-existing workflow formatting differences; no formatting rewrite was applied.
- A full `dotnet build CloudOrders.slnx --configuration Release --no-restore` was attempted but failed because a separate active `testhost` process (PID 29664) held IntegrationTests output DLLs. The focused build and architecture test above are unaffected. The process was not terminated because it is external concurrent work.

### Review-remediation concerns

- `actionlint` is not installed locally; run it in CI or an environment that provides it.
- Re-run the full solution build/test after the external IntegrationTests `testhost` process exits. Docker remains required for the Testcontainers integration suite.
