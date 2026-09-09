# Sprint 5 Task 1 implementation report

Status: DONE_WITH_CONCERNS

## TDD evidence

- RED: `dotnet test tests/CloudOrders.IntegrationTests/CloudOrders.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~OutboxLeaseIntegrationTests` failed at compilation because `ClaimAsync` and `MarkPublishedAsync` did not exist (expected).
- GREEN: the same focused command passed (1/1).
- Integration: `dotnet test tests/CloudOrders.IntegrationTests --configuration Release --no-restore` passed (90/90).
- Unit: `dotnet test tests/CloudOrders.UnitTests --configuration Release` passed (19/19).
- Build: `dotnet build src/CloudOrders.Migrations --configuration Release` passed with zero warnings/errors.

## Changes

Added SQL-backed outbox lease abstraction and implementation with atomic parameterized `UPDATE ... OUTPUT`, owner/token checks, conditional publish marking, renewal, attempt tracking, lease expiry, and filtered lease index. The event JSON now carries its version and traceparent metadata while preserving the stable EventId. Added migration `20260909213051_AddOutboxLeasing` and updated migration snapshot and history-count assertion.

## Migration inspection

Inspected `20260909213051_AddOutboxLeasing.cs`: adds nullable `LeaseOwner`, `LeaseToken`, and `LeaseExpiresAt` columns plus filtered `IX_OutboxMessages_Lease` on `(LeaseExpiresAt, EventId)` where `ProcessedAt IS NULL`; Down removes all changes.

## Commit

Commit: amended after additional coverage (see git history).

## Concerns

No broker publisher was added because it is explicitly out of scope for this task.

## Additional required coverage

- Added and passed `ClaimCapsAtomicBatchAtFiveHundredRows` (501 seeded rows, concurrent-safe 500-row cap).
- Added and passed `ExpiredLeaseCanBeReclaimedWithNewToken`; the initial red run exposed sub-second lease rounding to zero seconds, fixed by enforcing a one-second SQL lease duration minimum.
- Added and passed `ClaimPreservesCanonicalEventIdentityAndVersionedMetadata`, asserting payload EventId/orderId/type/version match persisted identity (the stable EventId is the broker MessageId source).
- Focused suite after fixes: 4/4 passed. Full integration suite after the extension: 90/90 passed.

## Reviewer follow-up

- Real `CreateAsync` integration coverage now asserts canonical payload `messageType` and `messageVersion`, with the DB version sourced from the event's canonical version property.
- Added concurrent 501-row claim coverage proving a 500 + 1 split.
- Renewal now uses conditional database UTC (`SYSUTCDATETIME()`) and rejects non-positive durations.
- Focused follow-up: 4/4 passed. Full integration follow-up: 90/90 passed.
