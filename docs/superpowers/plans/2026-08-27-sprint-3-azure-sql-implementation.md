# Sprint 3 Azure SQL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair the failed development deployment by provisioning secure Azure SQL and applying the committed EF migration before the API revision is promoted.

**Architecture:** Bicep composes pinned AVM SQL modules with the existing Container Apps resources. The API uses its system identity; a dedicated migration Job uses a user-assigned identity. A local administrator bootstrap creates Azure SQL contained users once; routine deployment runs the migration job then the API candidate.

**Tech Stack:** .NET 10, EF Core 10.0.10, Azure SQL Database serverless, Azure Container Apps Jobs, Bicep/AVM, GitHub Actions OIDC, Azure CLI.

## Global Constraints

- Create or modify only `ordersapp-development` and `ordersapp-test`; do not deploy, query-mutate, or configure production.
- Do not alter the subscription Owner assignment, Entra Global Administrator role, portal account, GitHub OIDC applications, or existing GitHub environment settings.
- Azure SQL is Microsoft Entra-only; application and migration connection strings contain no password or token.
- The API does not invoke `Database.Migrate()` or `EnsureCreated()`.
- Use General Purpose serverless, 0.5–1 vCores, auto-pause after 60 minutes, TLS 1.2+, and an `AllowAllWindowsAzureIps` exception tagged `owner=Robert Magowan`, `expiresOn=2026-09-10`, `removalSprint=7`.
- Keep the existing healthy API revision on traffic unless the migration and candidate smoke checks pass.

---

### Task 1: Build a migration runner and deployment contracts

**Files:**
- Create: `src/CloudOrders.Migrations/CloudOrders.Migrations.csproj`, `src/CloudOrders.Migrations/Program.cs`, `src/CloudOrders.Migrations/Dockerfile`
- Modify: `CloudOrders.slnx`, `Directory.Packages.props`, `tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj`
- Create: `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`

- [ ] Write `MigrationRunnerAppliesCommittedMigrations` against Testcontainers SQL; expect the process to apply `20260816221235_InitialSqlPersistence` and return zero.
- [ ] Add the console runner that resolves `ConnectionStrings:CloudOrders`, calls `Database.MigrateAsync()`, and exits non-zero on a missing connection or pending failure. It must not reference HTTP hosting.
- [ ] Add a Dockerfile that builds the runner and uses a non-root .NET 10 runtime image.
- [ ] Run `dotnet test tests/CloudOrders.IntegrationTests --filter FullyQualifiedName~MigrationRunnerTests` and verify GREEN.
- [ ] Commit: `feat: add SQL migration runner`.

### Task 2: Add AVM-backed Azure SQL and migration-job infrastructure

**Files:**
- Create: `infra/modules/sql-server.bicep`, `infra/modules/sql-database.bicep`, `infra/modules/migration-job.bicep`
- Modify: `infra/main.bicep`, `infra/avm-versions.md`, `infra/environments/{development,test,production}.bicepparam`, `tests/CloudOrders.ArchitectureTests/ArchitectureTests.cs`

- [ ] Add a failing architecture policy asserting `infra/main.bicep` declares `sqlServerName`, `sqlDatabaseName`, `migrationIdentityName`, and produces `sqlServerFqdn`, `databaseName`, and migration-job outputs.
- [ ] Pin the AVM SQL server/database module versions in `infra/avm-versions.md`; compose one logical server/database per non-production overlay. Set Entra-only admin from deployment parameters, public network enabled only with the named Azure-services firewall exception, TLS 1.2+, serverless SKU/range/auto-pause, and the required tags.
- [ ] Create a user-assigned migration identity and a Container Apps Job using its identity. Configure its non-secret managed-identity connection string; configure the existing API Container App with its own matching managed-identity connection string.
- [ ] Ensure production parameters set `deploySql=false`; `main.bicep` must reject an attempt to enable SQL for production in this sprint.
- [ ] Run Bicep lint/build and every parameter build; run the architecture policy GREEN.
- [ ] Commit: `feat: provision nonproduction Azure SQL`.

### Task 3: Add controlled database-principal bootstrap

**Files:**
- Create: `ops/Bootstrap-CloudOrdersSql.ps1`, `ops/Bootstrap-CloudOrdersSql.Tests.ps1`, `docs/operations/azure-sql-bootstrap.md`

- [ ] Write failing Pester tests proving the script rejects `production`, requires a valid non-production resource group/server/database, and emits SQL for API data roles plus migration schema roles without `db_owner` for the API.
- [ ] Implement the script to acquire a `https://database.windows.net/` token from the administrator Azure CLI session, create contained users for the API and migration identities, and grant exactly documented roles. It must use the temporary Entra administrator only for bootstrap and never print a token.
- [ ] Document the administrator prerequisites, firewall exception owner/expiry, SQL server identity/Graph permissions, rerun behavior, and Sprint 7 private-endpoint removal obligation.
- [ ] Run Pester and a development dry-run; inspect output for no passwords/tokens.
- [ ] Commit: `feat: add Azure SQL identity bootstrap`.

### Task 4: Orchestrate migration before API promotion

**Files:**
- Modify: `.github/workflows/deploy.yml`, `.github/workflows/bicep-validation.yml`, `README.md`
- Create: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`

- [ ] Write a failing workflow policy test requiring ordered `preview_sql`, `bootstrap_sql`, `run_migration`, and `deploy_release` stages, with `run_migration` preceding candidate waiting and no `--allow-insecure`/SQL password reference.
- [ ] Extend `deploy.yml` to build/push immutable API and migration images, run SQL what-if, start the migration Job, wait for a successful execution, and then deploy/check the API candidate. Pass only non-secret SQL connection settings from environment variables; publish sanitized job/revision/digest summaries and preserve rollback context.
- [ ] Extend Bicep validation to build the new modules and parameter overlays without Azure credentials.
- [ ] Run YAML formatting, actionlint, policy tests, Bicep lint/build/parameter builds, and the full Release .NET suite.
- [ ] Commit: `feat: deploy SQL migrations before API release`.

### Task 5: Prove the repaired development deployment and retain evidence

**Files:**
- Create: `docs/evidence/sprint-3/development-sql-deployment.md`, `docs/evidence/sprint-3/development-sql-smoke.md`

- [ ] Run reviewed development what-if and inspect the resource changes: logical server, database, identity, job, non-secret API setting, and no production mutation.
- [ ] Run the controlled bootstrap as the temporary Entra administrator; directly query `sys.database_principals` and role membership to prove API is not `db_owner`.
- [ ] Merge through the protected `feature/sprint3-azure-sql` → `development` PR, approve each deployment gate, and retain workflow/job/image/revision identities.
- [ ] A dedicated smoke-test agent validates default TLS, `/health/live`, `/health/ready`, first POST (201), exact idempotent replay (200 plus header), conflict (409), and direct SQL counts of exactly one `Orders`, `OutboxMessages`, and `IdempotencyRecords` row.
- [ ] Commit: `docs: record Sprint 3 SQL development evidence`.

## Plan Self-Review

- **Spec coverage:** Tasks 1–4 implement migration, SQL, managed identities, firewall controls, and workflow ordering; Task 5 proves the deployed data path and retention requirements.
- **Placeholder scan:** no unresolved execution placeholder is used; the only time-bound value is the explicit firewall expiry.
- **Type consistency:** `sqlServerFqdn`, `databaseName`, `migrationIdentityName`, and `ConnectionStrings__CloudOrders` are the shared deployment contract across Bicep, bootstrap, workflow, and tests.
