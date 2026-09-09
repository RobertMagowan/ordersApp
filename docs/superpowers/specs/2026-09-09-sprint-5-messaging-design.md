# Sprint 5 Messaging Design

## Purpose

Move an accepted order from `Pending` to `Processing` through a durable, observable, duplicate-safe asynchronous path. Sprint 5 adds the smallest production-shaped messaging slice; it does not add fan-out, production infrastructure, or private networking hardening.

## Decisions

- Use the transactional outbox pattern. The API commits the order, idempotency record, and `OrderCreatedIntegrationEventV1` outbox message in one SQL transaction.
- Use a timer-triggered publisher Function. It claims bounded unpublished rows through a 90-second SQL lease with a unique lease token, sends each row's persisted JSON payload, and marks a row published only when that token still matches. Only expired leases are reclaimed. A crash after send can cause a duplicate send after reclaim, which is safe because the stable `EventId` reaches the duplicate-safe inbox.
- Use a Service Bus-triggered processor Function. A unique `InboxMessage.EventId` constraint makes the Inbox insert an atomic claim; it and `Pending` to `Processing` occur in the same SQL transaction. A unique-key collision is a successful duplicate no-op.
- Use one Azure Service Bus **Standard** namespace shared by non-production environments. It contains exactly two queues: `development-orders` and `test-orders`.
- Keep environment boundaries through queue names, separate Function Apps, separate storage/configuration, and environment-scoped managed identities. Neither runtime receives permission for the other environment's queue.

## Message Contract and Failure Handling

`OrderCreatedIntegrationEventV1` contains required `EventId` (UUID), `OrderId` (UUID), `OccurredAtUtc` (UTC ISO-8601), `messageType`, `messageVersion`, and W3C `traceparent` metadata. `EventId` is the broker `MessageId`; the stored outbox JSON is reused unchanged for retries. Application logs include message/order identifiers and delivery count, but never payload secrets.

Queues use a 60-second lock and `maxDeliveryCount` of five. Functions have a five-minute execution limit; the processor renews its message lock while processing and retains a settlement margin before lock expiry. The publisher preserves pending rows if Service Bus is unavailable or a process ends after a send but before SQL acknowledgement. The processor claims inbox state transactionally, then completes the broker message only after SQL commit. On lock renewal/loss, execution timeout, or settlement failure it does not apply any further broker state; redelivery is made safe by the committed Inbox transaction. Transient broker or SQL failures abandon the message for retry. Invalid message type/version/payload are dead-lettered immediately; Service Bus dead-letters a transient failure after its fifth delivery, always with a sanitized reason. A DLQ replay retains the original `EventId` while assigning a new broker delivery identifier.

The publisher and processor emit structured lifecycle events and metrics for pending-outbox count and oldest age, publish success/failure, queue depth, delivery attempts, inbox duplicates, processing success/failure, DLQ count, and telemetry silence. The Sprint 5 evidence includes runbook steps for investigating stalled outbox rows and replaying a sanitized DLQ message; alert ownership and production thresholds remain a later, explicitly approved operational decision.

## Boundaries

Sprint 5 provisions only the shared Standard namespace, environment queues, minimum isolated Function hosts, required storage, and least-privilege queue roles for development and test. The development publisher receives `Azure Service Bus Data Sender` only for `development-orders`; its processor receives `Azure Service Bus Data Receiver` only for that queue. The test identities receive the equivalent roles only for `test-orders`. Deployment verification must prove cross-environment send and receive attempts are denied. Azure Functions hosting hardening, private networking, full local-platform reproducibility, topics/subscriptions, and production deployment remain later sprint work.

## Verification and Promotion

Local verification covers happy path, duplicate delivery, concurrent publisher drain, lease expiry/reclaim, broker outage/recovery, crash-after-send, lock loss, settlement failure, execution timeout, invalid payload, and poison/DLQ behavior using the pinned Service Bus emulator and Azurite. It must prove first request, exact idempotent replay, conflicting-payload rejection, concurrent same-key submission, and a failed transaction leave exactly one—or, on rollback, zero—order, idempotency, and outbox records. It must also prove crash-after-send/reclaim produces at most one order transition despite duplicate publication. Development verification inspects outbox, inbox, migration history, queue state, metrics, and `Pending → Processing`; it also proves the cross-environment queue deny policy. The Azure smoke set repeats the happy path and bounded failure drills. After promotion, a QA-only test-environment pass exercises successful, boundary, invalid, authorization, failure/recovery, state-integrity, concurrency, and regression paths. No test → master promotion is part of this sprint.
