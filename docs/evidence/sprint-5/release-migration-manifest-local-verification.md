# Sprint 5 release migration manifest local verification

Date: 2026-09-14 (UTC)

Verification commit: `eefe346f8ce3993b9fd16345a07202d6256f1765`

Descriptor: `ops/releases/current-release.json` (`releaseId` `20260913-current`)

## Complete local quality gate

The following commands were run from the repository root on the verification commit. Every command exited 0.

| UTC start | Command | Outcome |
| --- | --- | --- |
| 2026-09-14T01:50:00Z | `dotnet restore` | Passed; all projects up to date |
| 2026-09-14T01:50:00Z | `dotnet format --verify-no-changes` | Passed; no formatting changes required |
| 2026-09-14T01:50:00Z | `dotnet build --configuration Release` | Passed; 0 warnings, 0 errors |
| 2026-09-14T01:50:00Z | `dotnet test --configuration Release` | Passed; 171 tests, 0 failed, 0 skipped (19 unit, 43 architecture, 109 integration) |
| 2026-09-14T01:50:00Z | `az bicep build --file infra/main.bicep` | Passed; informational custom-config messages and Azure CLI update notice only |
| 2026-09-14T01:50:00Z | `az bicep lint --file infra/main.bicep` | Passed; informational custom-config messages and Azure CLI update notice only |

Focused migration-runner verification was also run with `dotnet test tests/CloudOrders.IntegrationTests --configuration Release --filter FullyQualifiedName~MigrationRunnerTests --no-restore`: 24 passed, 0 failed, 0 skipped.

## Persistent-state verification path

The SQL Server Testcontainers fixture used by `MigrationRunnerTests` creates an isolated database for each test. The release tests exercise this sequence:

1. Create an empty database and apply the migrations through `20260907142134_EnforceCustomerProfileOwnership`.
2. Run the migration runner with `--verify-release <descriptor>` and confirm sanitised JSON evidence.
3. Run it with `--apply-release <descriptor>` and confirm only the ordered authorised suffix is applied.
4. Run `--verify-release` again and confirm `outstanding` is empty.
5. Read `__EFMigrationsHistory` directly and assert the ordered baseline, including `20260909213051_AddOutboxLeasing`.
6. Inspect `dbo.OutboxMessages` and assert the leasing columns `LeaseExpiresAt`, `LeaseOwner`, and `LeaseToken` (plus the `IX_OutboxMessages_Lease` index) exist.

The suite also covers baseline equality as a no-op, code-only cumulative authorisation, unknown or missing history, a database ahead of the descriptor, unauthorised outstanding migrations, invalid descriptors, concurrent application, and connection-string redaction. Evidence output is limited to release ID, descriptor SHA-256, mode, migration IDs, baseline, and outstanding IDs; connection strings are not emitted.

## Azure development acceptance procedure

After the feature PR is merged to protected `development`, retain the workflow run URL and immutable merge/release SHA. Confirm the run records:

- descriptor SHA-256 and `releaseId` from `ops/releases/current-release.json`;
- immutable API and migration image digests;
- migration Job execution names and sanitised verify/apply/verify evidence;
- successful Azure Job completion, with `__EFMigrationsHistory` containing `20260909213051_AddOutboxLeasing`;
- API revision and image identity, with `GET /health/live` returning HTTP 200; and
- for a `deployApi:false` descriptor test, unchanged API revision, image digest, and traffic allocation.

Do not promote to `test` until this development evidence, independent review, and the required development destruction/acceptance gates pass. Production remains excluded from release-migration application.

## Limitations and evidence classification

No Azure development deployment was performed in this local verification session, so the Azure checks above remain an acceptance procedure rather than a claimed result. The local SQL checks require Docker and are represented by the passing Testcontainers integration suite; if Docker is unavailable in another environment, classify that run as `ENVIRONMENT_FAILURE`, preserve its output, restore Docker, and rerun the affected checks.
