# CloudOrders version-1 contracts

**Source handoff sections: 25-35**
**Contract version:** 1.0.0
**Repository normalization:** source references to `staging` mean the repository's `test` environment. The canonical promotion path is `feature/*` → `development` → `test` → `master`.

This is the repository-owned version of the version-1 contract pack. Every change to a named contract field, event, status, endpoint, retention period, security boundary, release-manifest field, or required evidence must update this document, its traceability row, and the relevant tests in the same pull request.

## 25. Business scope and status model

Version 1 is the complete vertical slice for order submission and asynchronous acceptance into processing. It does **not** implement payment, inventory reservation, fulfillment, shipping, cancellation, compensation, or placeholder variants of those behaviors.

| Status | Meaning |
|---|---|
| `Pending` | Order and Outbox were committed; asynchronous acceptance has not committed. |
| `Processing` | OrderProcessor consumed OrderCreated, won/confirmed the Inbox claim, and atomically accepted the order for downstream fulfillment. This is terminal in v1. |

The sole transition is `Pending → Processing`. Creation begins Pending; only ProcessOrderHandler makes the transition. Reprocessing is idempotently successful only when the same EventId is already in Inbox. All other transitions are rejected and tested. Broker retry, outbox-pending, duplicate, and DLQ are operational states, not Order statuses. Ordinary users see Received for Pending and Processing for Processing; authorized non-production diagnostics may show Stored, Publish attempted, Published, Delivered, Retried, Dead-lettered, and Replayed. Future Completed, Cancelled, or Failed statuses require real behavior and compensation rules.

## 26. API and compatibility

The API base is `/api/v1`; JSON is UTF-8 camelCase, timestamps are UTC ISO-8601, and statuses are strings. CI-generated OpenAPI is the client source. A verified External ID customer is the default product capability. Required delegated token `scp` values are bare `Orders.Read` and `Orders.Write`; `user.admin` is the sole elevated product role. The `/me` endpoint (`GET /api/v1/me`) requires `Orders.Read` and returns the caller's server-owned profile/customer reference. Authentication failures return 401; a valid caller missing the required scope or role returns 403; absent or foreign customer resources return 404 without disclosing existence.

### Orders

`POST /api/v1/orders` requires `Idempotency-Key: <UUID>` and accepts customerReference, productSku, and quantity. References are trimmed, invariant-uppercase normalized, then validated and used unchanged for authorization, persistence, and hashing. Unknown JSON members are rejected.

| Field | Constraint |
|---|---|
| customerReference | 1–64 letters, digits, dash, underscore |
| productSku | 1–64 letters, digits, dash, underscore, dot |
| quantity | integer 1–100 |
| Idempotency-Key | UUID |

First success is `201 Created` with Location; an exact replay is `200 OK` and `Idempotency-Replayed: true`; validation is 400; auth failures are 401/403; a key with different canonical payload is 409; rate limit is 429; unavailable synchronous dependency is 503. The canonical hash is SHA-256 of `v1|subjectId|customerReference|productSku|quantity`; the scope is `(SubjectId, IdempotencyKey)`. Authorization is re-evaluated for every replay. Store the initial 201 response status but deliberately return the original representation with 200 on replay. Retention is seven days and clients never automatically retry POST with a new key.

`GET /api/v1/orders/{orderId}` returns 200, 400 for malformed ID, 401/403 for policy failure, or 404 when absent/outside scope. `GET /api/v1/customers/{customerReference}/orders` uses opaque, versioned, HMAC-authenticated base64url cursor state; default pageSize 20, range 1–100; ordering `CreatedAt DESC, Id DESC`; invalid/modified/expired/unsupported cursor returns 400 with `errorCode=invalid_cursor`.

Live health is anonymous/process-only. Readiness is for Container Apps only and not surfaced through Static Web Apps. Non-production diagnostics require TestOperate and return correlation/stage information only. Default limits are 16 KiB body, POST 60/minute/subject burst 10, GET 600/minute/subject burst 50, and 15-second application timeout. Rate limiting is subject-partitioned and globally bounded; 429 includes Retry-After. Problem Details uses `application/problem+json` and includes type, title, status, errorCode, traceId, and errors—never stack traces, SQL, tokens, connection strings, internal hostnames, or raw exceptions.

`/api/v1` is backward compatible: optional response fields may be added, but existing types/semantics cannot change. Breaking API changes require `/api/v2` and an overlap window. CI rejects unapproved breaking OpenAPI diffs. Event `messageVersion` changes independently.

## 27. Schema, atomicity, and retention

Application tables use `dbo`; `testsupport` is non-production only. CustomerProfiles are keyed by immutable issuer plus object ID and own their opaque server-generated customer references. Orders holds `CustomerProfileId`, CustomerReference (nvarchar(64)) for v1 compatibility, ProductSku (nvarchar(64)), Quantity (1–100), Status constrained to Pending/Processing, CreatedAt, UpdatedAt, and RowVersion, indexed by `(CustomerProfileId, CreatedAt DESC, Id DESC)`. Idempotency is bound to actor/target CustomerProfile IDs. Idempotency request hashes are SHA-256 over the canonical request payload plus ActorCustomerProfileId and TargetCustomerProfileId. IdempotencyRecords has primary key `(ActorCustomerProfileId, TargetCustomerProfileId, IdempotencyKey)`; the actor and target CustomerProfile IDs are the durable authority boundary, so a key cannot replay across profiles. E1 legacy compatibility: existing `(SubjectId, IdempotencyKey)` records remain readable only for a retry that resolves to the same actor and target CustomerProfileId; E1 creates no subject-only hash or key.

OutboxMessages is keyed by EventId and holds aggregate/order ID, message type/version, payload, occurrence/creation timestamps, processed timestamp, attempts/error code, and W3C trace values. Its filtered pending index is `(CreatedAt, Id)` including message/version/occurred/attempt fields. InboxMessages is keyed by EventId and records Handler, last broker message ID, processed timestamp, and trace ID. IdempotencyRecords stores the actor/target CustomerProfile IDs, idempotency key, request hash, OrderId, initial response status/JSON, created and expiry timestamps. Order, Outbox, and Idempotency are inserted in a single transaction; duplicate-key races are re-read and classified as replay or conflict.

`testsupport.ScenarioLeases` contains RunId, ScenarioId, FaultType, optional order/event scope, owner, created/expiry times, expected/actual hit count, state, and RowVersion. It never stores arbitrary script, SQL, URL, exception type, or payload.

Defaults: Orders 365 days; processed Outbox and Inbox 90 days; Idempotency 7 days; TestSupport lease/event evidence 7 days (active lease ≤60 minutes); telemetry 30 days development/90 days test/production; Playwright artifacts 14 days success/30 days failure. SQL PITR is 7/14/35 days for development/test/production; production uses zone/geo redundancy where supported and 12-week weekly plus 12-month monthly long-term retention. Restore is exercised quarterly in test. Pending outbox, DLQ-related evidence, failed cleanup, and active incidents are retained until resolved. Cleanup is explicit, bounded, measured, alerted, and preserves incident evidence through explicit parent/child ordering.

## 28. Edge, network, and Functions

Azure Static Web Apps Standard hosts CloudOrders.Web and proxies same-origin `/api` calls to the Azure Container App API. The API remains externally HTTPS reachable only as needed for the linked backend; it is not advertised as the product origin. Linked identity is edge defense, not business authorization: API scopes/roles/customer scope/rate/body limits still apply. Pull-request SWA environments use local/ephemeral dependencies; full cloud E2E targets persistent development/test. TestSupport is a separate non-production Container App using exact-origin CORS and TestOperator; it has no production configuration.

Use separated workload subnets: Container Apps `/27`+, Functions Flex `/26`+, and private endpoints `/27`+. SQL, Service Bus, Function Storage, Key Vault, and ACR are private-endpoint-only. The API remains public-but-linked until the edge design is deliberately replaced.

OutboxPublisher and OrderProcessor are separate Linux Flex Consumption .NET 10 isolated Functions runtime 4, deployed as immutable packages (not containers). Each has distinct host storage/host ID, managed identity, minimum storage/Service Bus/SQL permissions, VNet integration, and no public inbound product endpoint. The publisher runs every 10 seconds with monitored timer, 8-minute maximum drain, 500-row chunk, one effective deployment per DB. Processor starts with 5-minute function timeout, manual settlement, batch 20/minimum 5/max wait 5 seconds, no prefetch, 5-minute queue lock, per-message scope/DbContext/transaction, bounded parallelism starting at four, 20-second per-message cap, 3-minute invocation cap, and settlement before lock margin. Start both at 2048 MB; publisher one effective instance and processor max 10 until load evidence changes it. Use one authoritative telemetry pipeline—never duplicate worker/host/export paths.

## 29. Observability contract

Use the exact structured fields `cloudorders.test_run_id`, `cloudorders.scenario_id`, `cloudorders.order_id`, `cloudorders.event_id`, `cloudorders.broker_message_id`, `cloudorders.replay_id`, `cloudorders.delivery_count`, `cloudorders.idempotency_result`, `cloudorders.outbox_result`, `cloudorders.inbox_result`, `cloudorders.settlement_result`, and `cloudorders.error_code`.

`service.name` identifies Web, Api, OutboxPublisher, OrderProcessor, or TestSupport.Api; `service.version` is the immutable artifact version; `deployment.environment.name` is development, test, or production. Lifecycle event names are a tested contract: E2EScenarioStarted, ApiOrderCommitted, OutboxPublishAttempt, OutboxPublished, OutboxMarkFailed, MessageReceived, InboxClaimed, InboxDuplicate, OrderProcessed, MessageCompleted, MessageAbandoned, MessageDeadLettered, MessageReplayed, FaultActivated, FaultHit, FaultExpired, E2EScenarioCompleted, E2EScenarioFailed. All available correlation fields accompany each stage. Changing a name/field updates the traceability manifest, KQL, workbook, alert, and test in one PR.

Healthy traces cover browser/API order creation and SQL commit; publisher SQL query, Service Bus send, and mark; then processor receive and Inbox/Order commit. Maintain KQL for run timeline, missing required stages, and outbox/DLQ investigation under `ops/kql/`, validating actual exported field/table names before freezing. Dashboards show API health, throughput, outbox age, publish outcome, queue/DLQ, processing, SQL, Functions, and E2E runs. Initial alert thresholds cover API 5xx (5%/5m), p95 (1s/10m), oldest outbox (2m), publisher no-progress (two cycles), any DLQ, processor failure (5%/5m), SQL failures (3/5m), Function absence (two periods), uncleared expired fault, and telemetry silence (10m); every alert has owner, environment, action group, dashboard/runbook, suppression, and resolve condition.

## 30. Scenario evidence

Every Playwright E2E scenario has a traceability row that states UI, SQL, broker, trace-stage, and alert behavior. The required baseline covers healthy order; idempotency replay and key/payload conflict; broker unavailable and crash-after-send; processor transient and poison failures; DLQ replay; API SQL outage; and unauthorized Lab access. Playwright fails a scenario when business assertions pass but required telemetry is absent after bounded ingestion.

## 31. Non-functional and load targets

Portfolio targets (not contractual SLA): 99.9% design availability; warm POST/GET p95 ≤500 ms and p99 ≤1 second; dev cold request ≤5 seconds; acceptance-to-Processing p95 ≤30 seconds; healthy outbox age <30 seconds; broker restoration ≤60 seconds plus drain; sustained 50 rps/15m and burst 200 rps/2m; processor baseline 100 msg/s; 10,000 maximum staged backlog drained within 10 minutes; initial compressed browser payload ≤2.5 MiB; desktop broadband LCP ≤2.5 seconds; approximate Azure SQL PITR RPO 10 minutes; geo-restore RPO up to one hour/RTO 12+ hours; dev budget alerts at 50/80/100%.

NBomber—not Playwright—runs warm/unique/concurrent-idempotency API load, outbox drain, processor scale, SQL pressure, broker recovery, and cold-start tests manually or scheduled against test, never ordinary production. It publishes results/metrics and stops at cost/dependency protection thresholds.

## 32. Identity, data, and supply chain

Per environment: public `CloudOrders-Web-<env>` SPA with PKCE; `CloudOrders-Api-<env>` exposes delegated `Orders.Read`/`Orders.Write`; non-production `CloudOrders-TestSupport-<env>` remains a separate future TestOperator capability. The default verified signed-in customer has no app role; `user.admin` is the sole elevated product role. Use separate registrations/deployment identities, exact redirect URIs, no SPA secret, ROPC, production test account, or broad tenant-wide application permission. Playwright state is protected/ignored/non-uploaded. Telemetry verification uses its own OIDC identity scoped only to non-production workspace query (prefer Log Analytics Reader) and no SQL, broker, TestSupport mutation, or production access.

Data is limited to opaque customer references, SKUs, identifiers, quantity, timestamps, and technical correlation IDs. Customer/order references and auth audit data are confidential; trace IDs internal; credentials secret; test data synthetic. Encrypt in transit/rest; avoid payload/customer data in metric dimensions; redact auth/cookies/connections/SQL parameters; require `E2E-` synthetic TestSupport references; audit privileged/test actions; and govern deletion/export/backups under the same classification. CI creates SBOM/dependency inventory, verifies lock files, scans secret/dependency/container/IaC inputs, pins reviewed Actions, records digests, blocks critical vulnerabilities, and documents exception owner/expiry/rationale/control.

## 33. Deployment and compatibility

Promotion is local → development → test → production. Production never consumes a mutable development artifact: the same immutable API image, Function packages, and WASM artifact move forward. Release sequence is non-destructive IaC, expand-compatible migration, backward-compatible API, compatible Functions, compatible frontend, smoke/E2E/observability, then cleanup after rollback window. Use expand/migrate/contract; no rename/drop in a release that stops reading the old shape; consumers support current and previous event versions; unsupported versions DLQ with reason; WASM has hashed immutable assets and revalidated index; v1 has no offline mutation queue; rollback does not assume destructive migration reversal. Sprint 4A E1 is the `AddCustomerProfileOwnershipExpand` migration-only release: it captures one started Container Apps Job execution, polls only that execution, deploys no API artifact, and fails if API revision, image digest, or traffic changes.

Every release manifest records Git SHA, image digest, Function package hash, frontend artifact hash, migration ID, Bicep deployment name, and OpenAPI/event contract versions.

## 34. Operational runbooks

Executable runbooks in `ops/runbooks/` name owner, prerequisites, least privilege, commands/queries, validation, rollback, and evidence. DLQ replay verifies root cause/Inbox state, creates ReplayId and unique `{EventId}:replay:{ReplayId}` broker message ID while preserving EventId/OriginalEventId, sends before completing DLQ copy, proves one Inbox/business result, and records evidence. Stuck outbox investigation restores dependencies/redeploys known-good publisher and never hand-sets ProcessedAt. Migration failure stops promotion, keeps compatible revision, captures state, rolls forward where possible, and only restores after explicit data-loss/RPO approval. Identity/network incidents restore minimum access/path—not Owner/db_owner—and record/remove temporary changes. Telemetry silence is isolated independently of service health. Restore/recovery validates all dependencies, compatible artifacts, integrity and smoke before writes, reconciles/replays safely, retains evidence, and records actual RPO/RTO. Rollback selects previous manifest, verifies schema compatibility, redeploys immutable artifacts, reruns health/order/async/telemetry checks, and opens corrective work.

## 35. Version-1 definition of done

Done requires tested business transitions; compatible API/OpenAPI/events; empty/upgrade migration validation; local happy/failure drills; explained Bicep build/validate/what-if; least-privilege negative access evidence; immutable deployment of Web/API/publisher/processor; unit/integration/contract/bUnit/Playwright/accessibility/security/load gates; matching UI/SQL/broker/telemetry evidence for healthy/retry/duplicate/DLQ/replay; exercised dashboards/alerts/runbooks; tested test restore/rollback; no TestSupport/fault/synthetic credentials in production; and recorded cost, retention, classification, ownership, and support handoff.
