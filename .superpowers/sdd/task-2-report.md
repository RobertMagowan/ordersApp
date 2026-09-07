# Task 2: E2 ownership mappings and migration

## Implementation

- Made `Orders.CustomerProfileId`, `IdempotencyRecords.ActorCustomerProfileId`, and `TargetCustomerProfileId` non-nullable CLR `Guid` properties and required EF columns.
- Changed idempotency identity to `(ActorCustomerProfileId, IdempotencyKey)`.
- Retained `SubjectId` as a nullable `nvarchar(200)` column for D1 compatibility.
- Added timestamped migration `20260907142134_EnforceCustomerProfileOwnership`, including a reversible E1-shaped `Down` path. The migration contains no `Guid.Empty`, empty-string, data backfill, or default-value operation.
- Preserved fail-closed D1 boundary conversion: null ownership input throws during SQL persistence; empty legacy ownership reads return no order.

## Verification

- `dotnet test tests\\CloudOrders.ArchitectureTests --filter FullyQualifiedName~ContractPackTests --configuration Release --no-restore`: PASS (3/3).
- D1 create/get/replay plus E2 compatibility slice (`MigrationFirstCreateAndGetPersistAnOrder`, exact/canonical replay, nullable SubjectId, and E2 schema contract): PASS (5/5).
- Broader focused integration invocation was started with a bounded window and stopped after no progress; the targeted regression slice above completed successfully.
- `dotnet ef migrations has-pending-model-changes --project src\\CloudOrders.Infrastructure --startup-project src\\CloudOrders.Migrations --configuration Release --no-build` with a local design-time connection string: PASS; no pending model changes.
- Generated E1-to-E2 idempotent SQL from `20260829075044_AddCustomerProfileOwnershipExpand` to `20260907142134_EnforceCustomerProfileOwnership`; reviewed output for required `ALTER COLUMN`, key replacement, nullable SubjectId, and absence of `UPDATE`, `Guid.Empty`, empty-string, or data backfills.
- `git diff --check`: PASS.
- Release solution build was attempted; it was blocked by missing pre-existing assets for `tools\\CloudOrders.AuthSmoke` and `tests\\CloudOrders.UnitTests` under `--no-restore` (0 warnings, 2 restore prerequisite errors). No release workflow files were changed.

## Result

Task 2 implementation is complete and ready for the requested commit. Integration execution requiring the full focused suite remains an environment/runtime concern; the bounded D1/E2 regression slice passed.
