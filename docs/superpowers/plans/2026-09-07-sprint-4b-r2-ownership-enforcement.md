# Sprint 4B R2 Ownership Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans task-by-task.

**Goal:** Make D1 ownership mandatory in Azure SQL while preserving D1 API behaviour.

**Architecture:** E2 makes the three profile identifiers required and changes the idempotency key to actor/key. `SubjectId` remains nullable until R4. A quiesced precondition transaction stops release before schema change when data is unsafe.

**Tech Stack:** .NET 10/C# 14, EF Core 10, Azure SQL, xUnit, PowerShell, GitHub Actions.

## Constraints

- Work on `feature/sprint4b-r2`; promote only to development and test.
- Retain D1's `profile:{ActorCustomerProfileId:N}` `SubjectId` behaviour.
- D1 is the sole post-E2 rollback image. Production is excluded.

### Task 1: Create E2 migration-contract tests

**Files:** `tests/CloudOrders.IntegrationTests/{CustomerProfileSqlIntegrationTests,MigrationRunnerTests}.cs`; `tests/CloudOrders.ArchitectureTests/ContractPackTests.cs`.

- [ ] Add red tests that E2 rejects null order, actor, and target ownership; rejects duplicate `(ActorCustomerProfileId, IdempotencyKey)`; and retains nullable `SubjectId` for D1 writes.
- [ ] Run the focused integration tests and confirm the new tests fail before E2 exists.
- [ ] Add migration-SQL assertions: exactly the three owner columns become required, the legacy key is replaced, and `SubjectId` is not dropped.
- [ ] Commit: `test: define Sprint 4B ownership migration contract`.

### Task 2: Implement E2 mappings and migration

**Files:** `src/CloudOrders.Infrastructure/Persistence/{OrderEntity,IdempotencyRecordEntity}.cs`, their configuration classes, `Migrations/*_EnforceCustomerProfileOwnership.*`, and the model snapshot.

- [ ] Make the three ownership properties required; configure the idempotency key as actor/key; leave `SubjectId` optional.
- [ ] Generate and inspect `EnforceCustomerProfileOwnership`; its Up migration makes the columns non-null and replaces the key, and its Down migration returns to E1 shape.
- [ ] Run focused integration tests, `has-pending-model-changes`, and an E1-to-E2 idempotent SQL script.
- [ ] Commit: `feat: enforce customer profile ownership`.

### Task 3: Add safe release controls

**Files:** `ops/Bootstrap-CloudOrdersSql.ps1`, its tests, `.github/workflows/deploy.yml`, `ops/releases/sprint-4b-r2.json`, and `docs/evidence/sprint-4b/r2-*.md`.

- [ ] Add red PowerShell tests for a one-transaction precondition probe: null order ownership, null actor/target ownership, and duplicate actor/key groups all block migration.
- [ ] Implement manifest validation, fail-closed ingress quiescence, current D1 digest/revision capture, exact job polling, and health-gated traffic restoration.
- [ ] Run PowerShell workflow contracts and architecture tests.
- [ ] Commit: `feat: gate Sprint 4B ownership release`.

### Task 4: Verify, review, and promote

- [ ] Run format, Release build, full tests, Bicep build/parameter builds, and `git diff --check`; store sanitised local evidence.
- [ ] Complete independent review and fix all Critical/Important findings.
- [ ] Merge to development; smoke-test migration history, constraints, D1 create/replay, health, active digest, and D1 rollback reference.
- [ ] Promote to test; conduct targeted destructive QA and D1 rollback proof; record evidence and stop before production.

## Self-Review

Tasks 1–2 cover schema behaviour, Task 3 covers pre-migration safety, and Task 4 covers required local, development, and test gates. No task removes `SubjectId` or makes a production change.
