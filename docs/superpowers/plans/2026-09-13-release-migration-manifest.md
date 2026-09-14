# Release Migration Manifest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Sprint-specific Azure SQL migration logic with an immutable, cumulative release descriptor that safely verifies and applies the exact schema required by a non-production release.

**Architecture:** `ops/releases/current-release.json` is the single release descriptor selected from the checkout SHA. A migration runner parses a supplied descriptor, verifies SQL history against its ordered baseline, and applies only an authorised outstanding suffix. GitHub Actions validates the descriptor before Azure mutation, passes descriptor identity to the existing migration Job, reconciles uncertain executions, and deploys the API only after the required schema is verified.

**Tech Stack:** .NET 10/C# 14, EF Core SQL Server migrations, xUnit/Testcontainers integration tests, Bash/Python/JQ, GitHub Actions, Azure Container Apps Jobs.

## Global Constraints

- Keep `feature/* → development → test → master` PR promotion; do not alter production deployment behaviour.
- Target `net10.0`; nullable enabled; warnings are errors; use file-scoped namespaces and four spaces.
- Never run migrations from API startup or call unrestricted `Database.MigrateAsync` in the release flow.
- Migration execution occurs only through the existing Container Apps Job identity; do not add SQL credentials or pipeline SQL roles.
- Reject malformed descriptor, unknown history, baseline holes, a target ahead of baseline, unknown precondition, or identity mismatch before API deployment.
- Preserve release commands, immutable SHA/image digest, Job execution identity, and sanitised SQL-history evidence in deployment output.

---

### Task 1: Define and test the immutable release descriptor contract

**Files:**
- Create: `ops/releases/current-release.json`
- Create: `ops/releases/release-schema.json`
- Modify: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`
- Modify: `tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj` only if the project does not already expose the JSON files as test content.

**Consumes:** The approved design and existing `ops/releases/sprint-4b-r2.json` semantics.

**Produces:** A versioned JSON contract with `releaseId`, `schemaVersion`, `environments`, `deployApi`, `requiredMigrationBaseline`, `authorisedMigrations`, `precondition`, `trafficPolicy`, and `compatibility`.

- [ ] **Step 1: Add failing architecture tests for the descriptor contract.**

```csharp
[Fact]
public void CurrentReleaseDescriptorMustDeclareAnOrderedCumulativeBaseline()
{
    using var document = JsonDocument.Parse(File.ReadAllText(CurrentReleasePath));
    var root = document.RootElement;
    Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
    Assert.True(root.GetProperty("requiredMigrationBaseline").GetArrayLength() > 0);
    Assert.Equal(JsonValueKind.Array, root.GetProperty("authorisedMigrations").ValueKind);
}
```

- [ ] **Step 2: Run the focused test and confirm it fails because the descriptor/schema are absent.**

Run: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --filter FullyQualifiedName~DeploymentWorkflowPolicyTests`

Expected: FAIL identifying missing `ops/releases/current-release.json`.

- [ ] **Step 3: Add a strict draft-2020-12 JSON schema and first descriptor.**

The schema must use `additionalProperties: false`, require all produced fields, make full EF IDs unique ordered arrays, restrict `precondition` to `none` or `ownership-read-only-transaction`, restrict `trafficPolicy` to `none` or `controlled-nonproduction-access`, and restrict `compatibility` to `api-compatible` or `maintenance-required`. The first descriptor must authorise `20260909213051_AddOutboxLeasing`, declare the complete existing EF baseline through that migration, allow only development/test, set `deployApi: true`, and exclude production.

- [ ] **Step 4: Extend policy tests for invalid JSON, duplicate IDs, unauthorised environments, and code-only cumulative authorisation.**

```csharp
[Theory]
[InlineData("unknown-precondition")]
[InlineData("unexpected-traffic-policy")]
public void ReleaseSchemaRejectsUnknownPolicyValues(string value) =>
    Assert.False(ValidateDescriptor(DescriptorWith(precondition: value)).IsValid);
```

- [ ] **Step 5: Run focused and architecture tests.**

Run: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release`

Expected: PASS with zero errors/warnings.

- [ ] **Step 6: Commit the contract.**

```powershell
git add ops/releases/current-release.json ops/releases/release-schema.json tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs
git commit -m "feat: define release migration descriptor"
```

### Task 2: Add bounded release verification and application to the migration runner

**Files:**
- Modify: `src/CloudOrders.Migrations/Program.cs`
- Modify: `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`

**Consumes:** `current-release.json` full baseline and authorisation fields from Task 1.

**Produces:** `--verify-release <path>` and `--apply-release <path>` modes that emit sanitised JSON evidence and never call unrestricted migration.

- [ ] **Step 1: Write failing integration tests for release verification and suffix application.**

```csharp
[Fact]
public async Task ApplyReleaseAppliesOnlyTheAuthorisedOutstandingSuffix()
{
    await using var database = await fixture.CreateEmptyDatabaseAsync();
    await RunRunnerAsync(database.ConnectionString, "--migration", "AddCustomerProfileOwnershipExpand");
    var result = await RunRunnerAsync(database.ConnectionString, "--apply-release", DescriptorPath);
    Assert.Equal(0, result.ExitCode);
    Assert.Contains("AddOutboxLeasing", result.StandardOutput, StringComparison.Ordinal);
}
```

Add cases for: baseline equality is a no-op; a code-only descriptor retains authorisation; unknown applied ID; a missing middle migration; database ahead of baseline; unauthorised outstanding ID; and invalid arguments. Assert connection strings never appear in error output.

- [ ] **Step 2: Run the focused tests and confirm the new mode fails as unsupported.**

Run: `dotnet test tests/CloudOrders.IntegrationTests --configuration Release --filter FullyQualifiedName~MigrationRunnerTests`

Expected: FAIL only in the new release-mode cases.

- [ ] **Step 3: Implement argument parsing and descriptor loading.**

Implement mutually exclusive modes: no arguments retains local development migration behavior only; `--ownership-precondition`; `--verify-release <absolute-or-repository-relative-json-path>`; and `--apply-release <path>`. Deserialize a private immutable `ReleaseDescriptor` record and reject files that fail every schema/invariant: known policies, distinct full IDs, authorisation subset of baseline, environment supplied by `DEPLOYMENT_ENVIRONMENT`, and only authorised environments.

- [ ] **Step 4: Implement ordered-history verification and bounded execution.**

Read `GetMigrations`, `GetAppliedMigrationsAsync`, and `GetPendingMigrationsAsync`; require applied history to equal an ordered prefix of the descriptor baseline and baseline IDs to exist in EF metadata. Compute `outstanding = baseline.Skip(applied.Count)`. Reject an outstanding ID not in `authorisedMigrations`. `--verify-release` outputs a JSON object containing `releaseId`, `descriptorSha256`, `mode`, `applied`, `baseline`, and `outstanding`, without any connection data. `--apply-release` uses `IMigrator.MigrateAsync` once per outstanding target in order, checks the observed appended delta equals `outstanding`, then emits the same evidence.

- [ ] **Step 5: Add precise safe failure categories.**

Map descriptor failure to `RELEASE_DESCRIPTOR_INVALID`, history/baseline failure to `MIGRATION_BASELINE_CONFLICT`, and execution mismatch to `MIGRATION_STATE_CONFLICT`; preserve existing SQL-safe error behavior.

- [ ] **Step 6: Re-run focused tests, then all integration tests.**

Run: `dotnet test tests/CloudOrders.IntegrationTests --configuration Release --filter FullyQualifiedName~MigrationRunnerTests`

Expected: PASS.

Run: `dotnet test tests/CloudOrders.IntegrationTests --configuration Release`

Expected: PASS with no test regressions.

- [ ] **Step 7: Commit runner behavior and tests.**

```powershell
git add src/CloudOrders.Migrations/Program.cs tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs
git commit -m "feat: apply release-authorized migrations"
```

### Task 3: Replace Sprint-specific workflow controls with descriptor-driven migration execution

**Files:**
- Modify: `.github/workflows/deploy.yml`
- Modify: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`
- Delete: `ops/releases/sprint-4b-r2.json` only after Task 1 contract tests prove historic migration controls no longer execute.

**Consumes:** Descriptor path/schema from Task 1 and runner modes from Task 2.

**Produces:** A single nonproduction migration flow that validates and binds the descriptor, runs verified jobs, reconciles timeouts, and blocks API deployment on ambiguity.

- [ ] **Step 1: Add failing workflow policy tests.**

```csharp
[Fact]
public void DeploymentWorkflowMustNotContainSprintSpecificMigrationNames()
{
    var workflow = File.ReadAllText(DeployWorkflowPath);
    Assert.DoesNotContain("EnforceCustomerProfileOwnership", workflow, StringComparison.Ordinal);
    Assert.Contains("current-release.json", workflow, StringComparison.Ordinal);
    Assert.Contains("--verify-release", workflow, StringComparison.Ordinal);
}
```

Cover descriptor validation before `az` mutation, `deployApi:false` revision/digest/traffic invariance, descriptor SHA/release SHA/image arguments, and explicit production exclusion.

- [ ] **Step 2: Run focused policy tests and confirm they fail on hard-coded Sprint 4 references.**

Run: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --filter FullyQualifiedName~DeploymentWorkflowPolicyTests`

Expected: FAIL on old manifest and migration names.

- [ ] **Step 3: Replace `validate_promotion_ref` manifest outputs and special E1 job.**

Check out the immutable SHA, validate `ops/releases/current-release.json` with `release-schema.json` using Python `jsonschema` installed explicitly in the job or an in-repository no-dependency validator, and publish descriptor hash, API decision, and permitted environment outputs. Remove special-case `sprint-4a-e1` routing; retain only an allowlisted ownership precondition in the generic migration stage.

- [ ] **Step 4: Make `run_migration` generic and deterministic.**

Before starting a Job, capture API revision/digest/traffic. Copy the Job template, set its image to the prepared immutable migration image, set arguments to `--verify-release /workspace/ops/releases/current-release.json` or `--apply-release /workspace/ops/releases/current-release.json`, and inject only non-secret descriptor SHA/release SHA/environment variables. Verify the execution template reports the expected image/args/env. Run verify, apply, then verify. Store execution names and sanitised output in `$GITHUB_STEP_SUMMARY`.

- [ ] **Step 5: Implement safe timeout reconciliation.**

On polling timeout, query the same execution once more; if unresolved, run a fresh read-only verification Job. Continue only when the baseline is confirmed; otherwise fail closed. Never start a second apply Job merely because a prior status timed out. For `deployApi:false`, assert revision/digest/traffic match the pre-run snapshot and skip `deploy_release`.

- [ ] **Step 6: Make deployment dependencies generic.**

Remove `migration_only` conditions and make `deploy_release` depend on successful descriptor validation and migration verification. Keep master/production out of release-migration application; preserve existing build, Bicep, Azure SQL preview/bootstrap, immutable image and smoke-test behavior.

- [ ] **Step 7: Run workflow and policy validation.**

Run: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release --filter FullyQualifiedName~DeploymentWorkflowPolicyTests`

Expected: PASS.

Run: `git diff --check; dotnet format --verify-no-changes; dotnet build --configuration Release`

Expected: all commands exit 0.

- [ ] **Step 8: Commit workflow migration.**

```powershell
git add .github/workflows/deploy.yml tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs ops/releases
git rm ops/releases/sprint-4b-r2.json
git commit -m "feat: run migrations from release descriptor"
```

### Task 4: Complete evidence, local verification, and release readiness

**Files:**
- Create: `docs/evidence/sprint-5/release-migration-manifest-local-verification.md`
- Modify: `docs/operations/sprint-delivery-workflow.md` only to name the descriptor evidence, not to alter promotion policy.

**Consumes:** Tasks 1–3.

**Produces:** Reproducible local and Azure-development acceptance evidence for the outbox schema release.

- [ ] **Step 1: Run the complete local quality gate.**

Run:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test --configuration Release
az bicep build --file infra/main.bicep
az bicep lint --file infra/main.bicep
```

Expected: every command exits 0; record command, timestamp, commit SHA, and output summary.

- [ ] **Step 2: Perform local persistent-state verification.**

Use the SQL Server integration fixture to create a database, run `--verify-release`, run `--apply-release`, run `--verify-release` again, and inspect `__EFMigrationsHistory` plus the `OutboxMessages` lease columns. Record only migration IDs, column names, pass/fail, and sanitised runner evidence.

- [ ] **Step 3: Record the development deployment acceptance procedure.**

Document the required post-merge checks: GitHub workflow descriptor hash/image/execution evidence, Azure Job success, `__EFMigrationsHistory` contains `20260909213051_AddOutboxLeasing`, API revision/image and `/health/live` return 200, and no API revision/traffic change on a `deployApi:false` descriptor test. State that test promotion follows only after this task’s development evidence and review gates pass.

- [ ] **Step 4: Commit evidence guidance.**

```powershell
git add docs/evidence/sprint-5/release-migration-manifest-local-verification.md docs/operations/sprint-delivery-workflow.md
git commit -m "docs: record release migration verification"
```

## Plan Self-Review

- Spec coverage: Tasks 1–3 implement immutable selection, cumulative suffix execution, identity-safe verification, precondition/API-invariance, and timeout recovery. Task 4 makes local and Azure evidence explicit.
- Placeholder scan: no `TODO`, `TBD`, or deferred implementation phrases remain.
- Type consistency: descriptor field names are identical across Tasks 1–3; runner modes are consistently `--verify-release` and `--apply-release`.
