# Sprint 5 Messaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver a duplicate-safe outbox-to-Service-Bus-to-inbox order transition that is locally testable and deployable to Azure development and test.

**Architecture:** Existing order, idempotency, and outbox writes remain atomic. A timer publisher leases SQL rows and sends immutable events; an Azure Functions processor atomically claims Inbox state and transitions the order. Azure Service Bus Standard is shared, but `development-orders` and `test-orders` remain identity-isolated.

**Tech Stack:** .NET 10/C# 14, EF Core 10/SQL Server, Azure Functions isolated worker, Azure.Messaging.ServiceBus, Service Bus emulator, Azurite, Bicep, xUnit/Testcontainers.

## Global Constraints

- Keep the promotion path `feature/* → development → test`; do not deploy to master.
- Original sends use `EventId` as `MessageId`; DLQ replay uses `{EventId}:replay:{ReplayId}`.
- Use database-UTC atomic `UPDATE ... OUTPUT` outbox claims, 90-second token leases, 500-row chunks, and conditional token marks.
- Use five-minute queue locks, five-minute host timeout, three-minute invocation maximum, 20-second per-message deadline, manual settlement, no prefetch, batch maximum 20/minimum 5/five-second wait, and bounded processor concurrency of four.
- Do not add topics, production resources, private-network hardening, secrets, startup migrations, or namespace/resource-group-wide data roles.

---

### Task 1: Durable outbox lease and message contract

**Files:**
- Modify: `src/CloudOrders.Infrastructure/Persistence/{OutboxMessageEntity.cs,CloudOrdersDbContext.cs,SqlIdempotentOrderStore.cs}`
- Create: migration under `src/CloudOrders.Infrastructure/Persistence/Migrations/`
- Modify: `src/CloudOrders.Contracts/Orders/OrderCreatedIntegrationEventV1.cs`
- Test: `tests/CloudOrders.IntegrationTests/OutboxLeaseIntegrationTests.cs`

**Produces:** `IOutboxLeaseStore.ClaimAsync`, `MarkPublishedAsync`, and a versioned payload whose EventId, order ID, type/version, traceparent, and canonical JSON are persisted.

- [ ] Write SQL integration tests that create two concurrent claimers; assert one atomic 500-row claim, stale-token update affects zero rows, expired lease can be reclaimed, and an EventId body equals original broker MessageId.
- [ ] Run the new tests first; expected result: compilation/test failure because the lease store and columns do not exist.
- [ ] Add lease owner/token/expiry, attempts, processed timestamp, canonical payload, and filtered pending/lease indexes; generate and inspect the migration.
- [ ] Implement `ClaimAsync` with parameterized database-UTC `UPDATE ... OUTPUT`, and conditional mark/renew operations; preserve send-after-crash duplicate safety.
- [ ] Re-run the focused SQL tests and `dotnet test tests/CloudOrders.IntegrationTests --configuration Release`.
- [ ] Commit: `feat: add durable outbox leasing`.

### Task 2: Publisher Function and local send proof

**Files:**
- Create: `src/CloudOrders.OutboxPublisher/{Program.cs,OutboxPublisherFunction.cs,host.json,CloudOrders.OutboxPublisher.csproj}`
- Modify: `CloudOrders.slnx`, `Directory.Packages.props`, `local/compose.yml`, `local/README.md`
- Test: `tests/CloudOrders.IntegrationTests/OutboxPublisherIntegrationTests.cs`

**Consumes:** `IOutboxLeaseStore`; **produces:** `OutboxPublisherFunction.RunAsync` and bounded send-then-mark behavior.

- [ ] Write failing tests for persisted payload reuse, broker failure retaining pending state, crash-after-send/reclaim, sender deadline, and distinct stale-token telemetry result.
- [ ] Add the isolated timer Function, Service Bus sender abstraction, explicit 8-minute drain deadline, 500-row bounded loop, and structured low-cardinality counters.
- [ ] Add pinned Service Bus emulator and Azurite configuration sufficient for sequential publisher flow; do not claim RBAC or lock behavior from the emulator.
- [ ] Run publisher integration tests plus local compose smoke; inspect SQL outbox states before/after send.
- [ ] Commit: `feat: publish leased outbox events`.

### Task 3: Inbox claim and order processor

**Files:**
- Create: `src/CloudOrders.OrderProcessor/{Program.cs,OrderProcessorFunction.cs,InboxStore.cs,host.json,CloudOrders.OrderProcessor.csproj}`
- Modify: `src/CloudOrders.Infrastructure/Persistence/{CloudOrdersDbContext.cs,OrderEntity.cs}` and migration
- Test: `tests/CloudOrders.IntegrationTests/OrderProcessorIntegrationTests.cs`

**Consumes:** `OrderCreatedIntegrationEventV1`; **produces:** unique `InboxMessage.EventId` claim plus one `Pending → Processing` transition.

- [ ] Write failing SQL tests for same EventId exact duplicate, conflicting EventId payload/order/type collision, transaction rollback, and concurrent processing.
- [ ] Add Inbox schema with unique EventId, handler/order/type/version/payload hash, and transactionally insert the Inbox record with the order transition.
- [ ] Implement raw `ServiceBusReceivedMessage` processing with `AutoCompleteMessages=false`, manual completion only after SQL commit, and duplicate no-op only when retained semantic identity matches.
- [ ] Run processor integration tests and assert one Inbox row and one order transition after duplicate deliveries.
- [ ] Commit: `feat: process order events through inbox`.

### Task 4: Failure, settlement, and DLQ replay

**Files:**
- Modify: `src/CloudOrders.OrderProcessor/{OrderProcessorFunction.cs,host.json}`
- Create: `src/CloudOrders.OrderProcessor/DlqReplayService.cs`, `ops/runbooks/replay-service-bus-dlq.md`
- Test: `tests/CloudOrders.IntegrationTests/{OrderProcessorFailureTests.cs,DlqReplayTests.cs}`

- [ ] Write failing tests for malformed body/EventId mismatch, lock loss before/after SQL commit, settlement failure, timeout, fifth transient delivery, and ReplayId reuse after uncertain completion.
- [ ] Configure the approved lock, host/invocation/message limits, batch/concurrency, no-prefetch, and lock renewal settings.
- [ ] Implement manual abandon/dead-letter/complete decisions. Ensure a replay preserves EventId/OriginalEventId but sends `{EventId}:replay:{ReplayId}` as MessageId before completing the DLQ source.
- [ ] Run fault-injection tests; document validation, least privilege, rollback, and evidence in the runbook.
- [ ] Commit: `feat: add safe messaging failure recovery`.

### Task 5: Non-production Azure messaging foundation and deployment

**Files:**
- Modify: `infra/main.bicep`, focused `infra/modules/` files, development/test parameter overlays, `.github/workflows/deploy.yml`
- Create: Function package/deployment workflow scripts and `docs/evidence/sprint-5/development-verification.md`
- Test: `tests/CloudOrders.ArchitectureTests/MessagingDeploymentPolicyTests.cs`

- [ ] Write failing architecture/Bicep contract tests for one Standard namespace, two queues, disabled local auth, four runtime identities, queue-scoped sender/receiver roles, replay identity, managed host storage/SQL access, and no cross-environment role assignment.
- [ ] Add Bicep resources and deterministic Function package manifests. Configure runtime identities to use `fullyQualifiedNamespace`; create SQL contained users with application data permissions only.
- [ ] Run `az bicep build`, lint, environment parameter builds, and reviewed development what-if before merging.
- [ ] Merge through development; verify Azure SQL rows, queue state, platform metrics, runtime identity access, cross-environment deny behavior, healthy flow, lease reclaim, lock/settlement failures, and DLQ replay. Record immutable identifiers and evidence.
- [ ] Commit focused infrastructure/package/evidence checkpoints: `infra: provision nonproduction messaging` and `docs: record Sprint 5 development verification`.

### Task 6: Test promotion and QA closure

**Files:**
- Create: `docs/evidence/sprint-5/{test-qa.md,review.md}`
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`

- [ ] Independently review the complete feature diff; fix every Critical/Important finding on a fresh `feature/*` remediation branch with re-tests.
- [ ] Promote the exact development merge to test through a PR; verify test deployment, immutable packages, smoke, queue metrics, and cross-environment denial.
- [ ] Have QA exercise happy, replay/conflict, duplicate delivery, corrupt input, transient recovery, crash-after-send, lease reclaim, lock/settlement failure, timeout, poison/DLQ/replay, state integrity, and regression paths in Azure test.
- [ ] Record defects and retests; update roadmap status only after every QA result passes. Do not create a test → master PR.
- [ ] Commit: `docs: close Sprint 5 messaging assurance`.

## Plan Self-Review

- Coverage: Tasks 1–4 implement every persistence, publishing, processing, failure, replay, and local-proof requirement; Task 5 covers Azure identity/IaC/development proof; Task 6 covers independent review and test QA.
- Dependencies: Tasks 2–4 depend on Task 1; Task 3 depends on Task 2 event contract; Task 4 depends on Task 3 settlement path; Tasks 5–6 depend on the complete local path.
- No production scope, secrets, startup migration, broad roles, or ambiguous implementation gap remains.
