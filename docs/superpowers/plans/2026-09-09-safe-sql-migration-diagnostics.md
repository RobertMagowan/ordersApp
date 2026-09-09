# Safe SQL Migration Diagnostics Implementation Plan

**Goal:** Expose non-secret SQL exception metadata from the migration runner so Azure authentication failures can be diagnosed without logging credentials or connection strings.

**Architecture:** Retain the existing failure category and redaction policy. For `SqlException`, add only the numeric error number, state, and class; all other exception handling remains unchanged.

**Tech Stack:** .NET 10, C# 14, Microsoft.Data.SqlClient, xUnit.

## Task 1: Safe SQL exception metadata

**Files:**

- Modify: `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`
- Modify: `src/CloudOrders.Migrations/Program.cs`

- [ ] Add a failing integration assertion that a deliberately invalid SQL connection logs the SQL exception number/state/class and does not log its connection string.
- [ ] Run `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~MigrationRunnerFailsWhenMigrationCannotConnect` and confirm the new assertion fails because `detail=Unavailable` is emitted.
- [ ] Update `GetSafeFailureDetail` to return only `SqlErrorNumber`, `SqlErrorState`, and `SqlErrorClass` for `SqlException` instances.
- [ ] Re-run the focused test and then the full solution test suite.
- [ ] Commit with `fix: expose safe SQL migration diagnostics`.
