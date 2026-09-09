# Sprint 5 Messaging Design

## Purpose

Move an accepted order from `Pending` to `Processing` through a durable, observable, duplicate-safe asynchronous path. Sprint 5 adds the smallest production-shaped messaging slice; it does not add fan-out, production infrastructure, or private networking hardening.

## Decisions

- Use the transactional outbox pattern. The API commits the order, idempotency record, and `OrderCreatedIntegrationEventV1` outbox message in one SQL transaction.
- Use a timer-triggered publisher Function. It drains a bounded batch of unpublished rows, sends each row's persisted JSON payload, and marks a row published only after Service Bus acknowledges it.
- Use a Service Bus-triggered processor Function. It inserts an Inbox claim before applying `Pending` to `Processing`; a duplicate `EventId` is a successful no-op.
- Use one Azure Service Bus **Standard** namespace shared by non-production environments. It contains exactly two queues: `development-orders` and `test-orders`.
- Keep environment boundaries through queue names, separate Function Apps, separate storage/configuration, and environment-scoped managed identities. Neither runtime receives permission for the other environment's queue.

## Message Contract and Failure Handling

`OrderCreatedIntegrationEventV1` contains stable `EventId`, `OrderId`, `OccurredAtUtc`, `messageType`, `messageVersion`, and W3C `traceparent` metadata. The stored outbox JSON is reused unchanged for retries. Application logs include message/order identifiers and delivery count, but never payload secrets.

The publisher preserves pending rows if Service Bus is unavailable or a process ends after a send but before SQL acknowledgement. The processor claims inbox state transactionally, then completes the broker message only after SQL commit. Transient broker or SQL failures abandon the message for retry. Invalid message type/version/payload and exhausted deliveries are dead-lettered with a sanitized reason.

## Boundaries

Sprint 5 provisions only the shared Standard namespace, environment queues, minimum isolated Function hosts, required storage, and least-privilege queue roles for development and test. Azure Functions hosting, private networking, full local-platform reproducibility, topics/subscriptions, and production deployment remain later sprint work.

## Verification and Promotion

Local verification covers happy path, duplicate delivery, broker outage/recovery, crash-after-send, invalid payload, and poison/DLQ behavior using the pinned Service Bus emulator and Azurite. Development verification inspects outbox, inbox, migration history, queue state, and `Pending → Processing`; the Azure smoke set repeats the happy path and bounded failure drills. After promotion, a QA-only test-environment pass exercises successful, boundary, invalid, authorization, failure/recovery, state-integrity, concurrency, and regression paths. No test → master promotion is part of this sprint.
