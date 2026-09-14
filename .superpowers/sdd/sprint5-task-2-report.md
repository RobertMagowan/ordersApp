# Sprint 5 Task 2 report

## Status

Implemented the isolated Azure Functions outbox publisher with a bounded eight-minute drain, 90-second leases, 500-row claims, send-then-conditional-mark ordering, stable EventId message IDs, and distinct stale-token results. The 2026-09-14 lease remediation is committed as `ea06d95` (base `29dafa5`), followed by explicit unavailable metrics in `806c63d`. Local remediation verification is complete; release/promotion and emulator-to-broker acceptance are not claimed.

## TDD evidence

The original publisher implementation was test-first. For this remediation, the inherited incomplete publisher test edits were preserved and strengthened before production changes. The revised publisher suite failed 5/9 against the original implementation: cancellation was not observed at the lease/send/mark deadline, and a sender returning after 90 seconds was incorrectly marked successful. After the fix, all 9 publisher cases passed.

Deterministic manual-time tests cover cancellation at 85 seconds with zero or 30 seconds of renewal latency, a 60-second send leaving only 25 seconds for the mark, late successful sender return preventing mark, and the eight-minute drain taking precedence when only ten seconds remain. The crash/reclaim test proves no resend one tick before expiry, then advances to expiry and resends the exact original payload/EventId with a different token.

A complementary SQL-backed publisher test reads the persisted EventId/body, simulates termination after send but before mark, proves a second publisher cannot claim the active lease, sets its expiry into the database-UTC past, and explicitly verifies `LeaseExpiresAt <= SYSUTCDATETIME()` before retry. It then proves identical resend, a new token, successful conditional marking, `AttemptCount = 2`, no pending row, and cleared lease fields. The real SQL lease store is used throughout; only the broker sender and crash boundary are substituted.

## Changes

- Added `src/CloudOrders.OutboxPublisher` Function app, timer trigger, sender abstraction, Azure Service Bus adapter, DI, host settings, and package references.
- Added pinned Azurite, Service Bus emulator, and SQL Server services plus sequential-proof documentation under `local/`.
- Added focused publisher tests and solution/project registration.
- Review remediation: one `TimeProvider`-backed cancellation timer now covers renewal, send, and conditional mark together. Its deadline is the earlier of the drain deadline or 85 seconds from before renewal begins. Starting before renewal includes query latency and avoids comparing host and database clocks; the five-second margin ends the operation before the database's renewed 90-second lease expires. Cancellation is checked after renewal and send, so a late successful return cannot initiate the next operation. Lease-budget failures do not incorrectly report the eight-minute drain as exhausted.

## Lease-remediation verification (historical)

Local verification on 2026-09-14, bound to the code/test tree committed in `ea06d95`; metrics-remediation verification below supersedes these counts:

- `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --filter FullyQualifiedName~OutboxPublisherIntegrationTests --no-restore`: PASS (9/9).
- `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~OutboxPublisherIntegrationTests|FullyQualifiedName~OutboxLeaseIntegrationTests'`: PASS (16/16, including SQL-backed crash/reclaim).
- `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --no-build --no-restore`: PASS (120/120).
- Unit and architecture project runs with `--configuration Release --no-build --no-restore`: PASS (19/19 and 43/43 respectively). Solution total at that revision: **182 tests**.
- `dotnet test CloudOrders.slnx --configuration Release --no-build --no-restore`: PASS (182/182: 19 unit, 43 architecture, 120 integration).
- `dotnet build CloudOrders.slnx --configuration Release --no-restore`: PASS (0 warnings, 0 errors).
- `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`: PASS.
- `git diff --check`: PASS.

Historical compose evidence remains unchanged: configuration validation passed, but the earlier compose startup/image-download attempt was cancelled before broker proof. It was not rerun as part of this narrowly scoped remediation.

## Concerns and limits

The emulator configuration is intentionally documented as sequential smoke coverage only; it is not evidence for Azure RBAC, lease locking, multi-instance concurrency, or production Service Bus Standard lock behavior. SQL-backed state inspection passed here, but a real SQL/emulator broker-send proof remains outstanding. The pre-existing untracked task brief was preserved and not included in the remediation commit.

## Local compose port remediation

The manual compose smoke was initially blocked because host port `5672` was already occupied by an unrelated existing container. The root cause was the fixed mapping `5672:5672` in `local/compose.yml`; the Service Bus emulator container itself must continue listening on `5672`.

The local-only remediation makes the published host port configurable with `SERVICEBUS_HOST_PORT`, defaulting to `5672` for existing users while retaining the container port at `5672`. Setting `SERVICEBUS_HOST_PORT=5673` allows the smoke stack to start alongside the unrelated container. This does not change application behavior, queue configuration, production infrastructure, or the emulator's container port.

Cancellation is cooperative and cannot retract a message already accepted by the broker. Timeout or crash can therefore still cause intentional at-least-once redelivery with the original EventId; downstream idempotency remains required. The sender and SQL APIs receive the shared deadline token; no detached send/mark work or startup migration was introduced.

The required read-only resume checks also ran. `ops/Test-SprintDelivery.ps1` reported pre-existing workflow contract failures, and `Invoke-SprintDelivery.ps1 -Reconcile -WhatIf` reported missing authoritative deployment snapshots. No delivery lifecycle state, Azure resources, credentials, or production infrastructure was changed; those workflow/release issues are outside this remediation.

## Prior review history (superseded verification)

Earlier reviews added renewal before every send, cancellation propagation, persisted SQL pending/oldest-age metrics, low-cardinality `OutboxPublisherCounters`, and local emulator configuration. Those changes remain.

The prior report's claim of a true expired-lease publisher reclaim test was inaccurate: its fake returned a second token by claim-call count without an expiry predicate. The earlier per-operation cancellation also used the eight-minute drain rather than bounding send plus mark by the 90-second lease. This remediation replaces both inadequate proofs; all earlier test counts are superseded by the fresh verification above.

## Metrics availability remediation

Final review identified that metrics deadline exhaustion fabricated `Pending = 0` and zero oldest age. Pending count and oldest age are now nullable in both the drain result and counters. An unavailable query leaves both null and emits `metricsStatus=unavailable`; a successful query emits `metricsStatus=available` with the measured values, including legitimate zero backlog. Deadline and query-failure warning outcomes are fixed, low-cardinality values. Publish success/failure counters are retained even if metrics cannot be collected, and caller cancellation continues to propagate. Lease/send/mark timing was not changed.

TDD: five new cases were observed failing against the old implementation before the fix. Deterministic time advancement covers deadline exhaustion before the metrics query and during it; both previously logged false zero backlog. Additional tests cover a query exception and measured empty/nonempty backlog. Assertions inspect both returned values and the structured counter log.

During full regression, an existing cancellation-observer test race surfaced: cancellation of `Task.Delay` could resume its continuation and dispose a separate observer registration before that registration ran. Tests now inspect the propagated sender/mark token directly at the simulated deadline and still await drain completion. This removes the observer scheduling race without relaxing the lease assertions. A rebuild attempted while the old test host still held its DLL also hit a Windows file lock; verification was rerun after that host exited. Five consecutive publisher-only reruns passed (14/14 each).

Latest verification on 2026-09-14, bound to code/test commit `806c63d`:

- Publisher filter with `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~OutboxPublisherIntegrationTests`: PASS (14/14, plus five consecutive reruns).
- Publisher and SQL lease filters with `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~OutboxPublisherIntegrationTests|FullyQualifiedName~OutboxLeaseIntegrationTests'`: PASS (21/21).
- `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --no-build --no-restore`: PASS (125/125).
- Unit and architecture project runs with `--configuration Release --no-build --no-restore`: PASS (19/19 and 43/43). Total across the three projects: **187 passing tests**.
- `dotnet build CloudOrders.slnx --configuration Release --no-restore`: PASS (0 warnings, 0 errors).
- `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`: PASS.
- `git diff --check`: PASS.
