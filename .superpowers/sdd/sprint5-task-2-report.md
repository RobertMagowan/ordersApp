# Sprint 5 Task 2 report

## Status

Implemented the isolated Azure Functions outbox publisher with a bounded eight-minute drain, 90-second leases, 500-row claims, send-then-conditional-mark ordering, stable EventId message IDs, and distinct stale-token results.

## TDD evidence

Added `OutboxPublisherIntegrationTests` before the publisher implementation. The initial focused invocation could not compile because `CloudOrders.OutboxPublisher` did not exist (red). After the minimal implementation, the focused suite passed 3/3 tests covering persisted payload reuse/original EventId, broker failure preserving pending state, and stale-token telemetry classification. Existing lease integration tests cover reclaim/crash-after-send semantics.

## Changes

- Added `src/CloudOrders.OutboxPublisher` Function app, timer trigger, sender abstraction, Azure Service Bus adapter, DI, host settings, and package references.
- Added pinned Azurite, Service Bus emulator, and SQL Server services plus sequential-proof documentation under `local/`.
- Added focused publisher tests and solution/project registration.

## Verification

- `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --filter FullyQualifiedName~OutboxPublisherIntegrationTests --no-restore`: PASS (3/3).
- `dotnet test CloudOrders.slnx --configuration Release --no-build`: PASS (175 total: 19 unit, 43 architecture, 113 integration).
- `dotnet build CloudOrders.slnx --configuration Release --no-restore`: PASS (0 warnings, 0 errors).
- `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`: PASS after formatting imports.
- `git diff --check`: PASS.
- `docker compose -f local/compose.yml config`: PASS. Compose startup was attempted, but image downloads were still in progress and were cancelled; no broker or SQL state inspection was possible in this environment.

## Concerns and limits

The emulator configuration is intentionally documented as sequential smoke coverage only; it is not evidence for Azure RBAC, lease locking, multi-instance concurrency, or production Service Bus Standard lock behavior. A real SQL/emulator send proof remains to be run where the required images and migration environment are available. The pre-existing untracked task brief was preserved.

## Review remediation

Added renewal before every send, deadline-linked cancellation for claim, renew, send, and conditional mark, and low-cardinality `OutboxPublisherCounters` (pending, oldest pending age, publish success/failure) with aggregate logs containing no EventId dimensions. Added a sender-cancellation deadline test and renewal assertion; existing send-then-failure coverage models crash-after-send pending retention and Task 1 SQL tests prove reclaim.

Added `local/servicebus-config.json` with the `orders` entity and `MaxDeliveryCount`, `local/local.settings.json.example`, a compose volume mount, and an executable `func start --csharp` path. No real credentials are included.

Remediation verification: focused publisher 4/4 PASS; full solution 175/175 PASS (19 unit, 43 architecture, 113 integration); Release build PASS (0 warnings/errors); format verify PASS; `git diff --check` PASS.

## Second review remediation

Added `GetMetricsAsync` to the lease-store contract and a parameterized SQL Server query using `ProcessedAt IS NULL`, `COUNT_BIG`, `MIN(CreatedAt)`, and database `SYSUTCDATETIME()`. Drain telemetry now reports those persisted pending/oldest-age metrics, not local failure counts or fabricated durations.

Added a true two-drain crash/reclaim boundary test: first send succeeds and mark simulates process termination; a new expired lease token is reclaimed, the exact persisted payload/EventId is sent again, and the second conditional mark succeeds. The deadline test now uses a controllable time provider and a sender that waits on the propagated cancellation token until the bounded deadline cancels it.

Final verification: focused publisher 5/5 PASS; full solution 177/177 PASS (19 unit, 43 architecture, 115 integration); Release build PASS (0 warnings/errors); format verify PASS; `git diff --check` PASS.
