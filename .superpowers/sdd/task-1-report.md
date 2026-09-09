# Task 1 report: Define the E2 schema contract

## Changed files

- `tests/CloudOrders.IntegrationTests/CustomerProfileSqlIntegrationTests.cs`
  - Added an E2 compatibility test proving a D1-style write with nullable `SubjectId` is accepted once E2 exists.
- `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`
  - Required the committed E2 migration and added direct SQL metadata assertions for the three required owner columns, nullable `SubjectId`, and `(ActorCustomerProfileId, IdempotencyKey)` primary-key ordering.
- `tests/CloudOrders.ArchitectureTests/ContractPackTests.cs`
  - Added migration-source assertions requiring the E2 ownership alterations, nullable legacy subject, primary-key replacement, and no `SubjectId` drop.

No production mappings, migrations, or snapshots were changed.

## Verification

- `dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter E2Migration -v normal`
  - **RED (expected):** 1 failed. `Assert.Single()` reported that no `*_EnforceCustomerProfileOwnership.cs` migration exists.
- `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --filter "E2|MigrationRunnerAppliesCommittedMigrations" -v minimal`
  - **RED (expected):** 3 failed. The migration runner binary was not present in the required Release output for the runner tests; the direct E2 compatibility test reached SQL and failed because E1 still has non-null `SubjectId`.
- `git diff --check`
  - Passed.

The failures are meaningful pre-E2 failures: the E2 migration is intentionally absent and the E1 schema rejects the D1-compatible null-subject write. The tests compile successfully.

## Self-review

- Tests are limited to the three files named in the brief.
- Assertions cover all required E2 columns, legacy `SubjectId` retention/nullability, key transition, and migration presence.
- The integration compatibility write supplies valid profile/order ownership values and only leaves the legacy subject null.
- No implementation or migration was added ahead of Task 2.
- Existing unrelated modification to `.superpowers/sdd/progress.md` was preserved and excluded from this commit.
