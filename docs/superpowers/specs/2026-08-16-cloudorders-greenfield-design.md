# CloudOrders Greenfield Design

**Status:** Approved direction; execution inputs are resolved at the phase that needs them.  
**Repository root:** `C:\repos\OrderApp`  
**Intended GitHub repository:** `ordersApp`  
**Primary source:** `CLOUDORDERS_HANDOFF.md`, with sections 25–35 authoritative when earlier sections differ.

## Objective and precedence

Build CloudOrders as a portfolio-quality .NET 10 and Azure system that accepts an order, commits it with an outbox event, publishes asynchronously, and idempotently advances it from `Pending` to `Processing`. The local repository is greenfield: the previous handoff describes a target design, not existing code. Work therefore begins with repository bootstrap rather than the handoff's former “resume at Function hosting” instruction.

Direct user requirements override the handoff. Current stable Microsoft guidance overrides stale platform details, but any resulting architectural change must be recorded in an ADR. The implementation remains incremental and pauses at the handoff's phase/subsection gates.

## Selected approach

Use a contract-first, vertical-slice build. Establish repository policy and executable contracts first, then implement the domain/API/database path, asynchronous messaging, local reproducibility, and Azure deployment in dependency order. This avoids building Function hosting against nonexistent application code while retaining the handoff's production-grade security, reliability, and observability requirements.

The repository will not add services merely for novelty. In particular, version 1 does not use Durable Functions, Event Hubs, Redis, a PWA mutation queue, or a second region. Docker Compose remains the local infrastructure mechanism; .NET Aspire may be evaluated later only through an ADR with a demonstrated benefit.

## Repository and solution organization

The root contains `CloudOrders.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `AGENTS.md`, and `README.md`.

```text
src/
  CloudOrders.Domain/
  CloudOrders.Application/
  CloudOrders.Infrastructure/
  CloudOrders.Contracts/
  CloudOrders.Api.Client/
  CloudOrders.Api/
  CloudOrders.Web/
  CloudOrders.OutboxPublisher/
  CloudOrders.OrderProcessor/
  CloudOrders.TestSupport.Api/       # local/development/test only
tests/
  CloudOrders.UnitTests/
  CloudOrders.IntegrationTests/
  CloudOrders.EndToEndTests/
  CloudOrders.Web.Tests/
  CloudOrders.Playwright/
  CloudOrders.LoadTests/
infra/  local/  ops/  docs/  .github/workflows/
```

The repository identity is `ordersApp`; product, namespace, solution, and Azure workload names use `CloudOrders`. The GitHub remote is created only with the user's account/organization approval.

## Modern .NET baseline

Target `net10.0` with the current stable .NET 10 SDK and stable C# 14. Pin the SDK in `global.json`; enable nullable reference types, implicit usings, deterministic builds, analyzers, and warnings as errors in CI. Use central package management and verify every package against .NET 10 before pinning it. Prefer modern language features when they improve clarity or safety; do not use unfamiliar syntax solely to demonstrate novelty.

ASP.NET Core uses built-in dependency injection, options validation at startup, Problem Details, OpenAPI generation, rate limiting, health checks, authorization policies, and OpenTelemetry. EF Core 10 targets SQL Server/Azure SQL. Azure Functions use runtime 4, the .NET isolated worker model, and current compatible worker/extension packages.

## Application and reliability architecture

`CloudOrders.Api` is a conventional ASP.NET Core API deployed as a non-root Azure Container App. `POST /api/v1/orders` atomically inserts `Orders`, `OutboxMessages`, and `IdempotencyRecords`; it never publishes directly to Service Bus. The explicit `OrderCreatedIntegrationEventV1` is independent of EF entities.

`CloudOrders.OutboxPublisher` is a timer-triggered Linux Flex Consumption Function. It drains bounded SQL chunks, creates broker-size-aware batches, publishes first, and marks rows processed second. `CloudOrders.OrderProcessor` is a separate Service Bus-triggered Flex Function. Each message receives its own scope, `DbContext`, Inbox claim, and SQL transaction, followed by explicit Complete, Abandon, or DeadLetter settlement.

At-least-once delivery is intentional. `EventId` remains the stable business occurrence and Inbox key; replay uses a distinct broker `MessageId`. API idempotency uses `(SubjectId, IdempotencyKey)` and a canonical payload hash. Sections 25–27 of the handoff define the version-1 status, HTTP, schema, and retention contracts.

## Azure platform and security

Azure Static Web Apps Standard hosts standalone Blazor WebAssembly and proxies same-origin `/api` traffic to the linked API Container App. Azure SQL, Service Bus, Function storage, Key Vault, and runtime ACR pulls use private endpoints after bootstrap. ACR public access remains disabled; its single firewall exception is the `AzureServices` trusted-services bypass contract for the system-assigned ACR Task, Defender scanning when enabled, and server-side ACR import during promotion. Container Instances and Machine Learning are neither deployed nor authorized against the registry. Runtime access uses managed identities and least-privilege data-plane/SQL roles; no runtime identity receives `db_owner`.

Bicep is the infrastructure source of truth, split into foundation, application, and observability entry points with environment parameter files. Stable API versions and regional support are rechecked immediately before pinning. GitHub Actions uses OIDC, protected environments, immutable artifacts, explicit concurrency, and promotion of identical artifacts through development, test, and production; `test` is the staging-equivalent environment. Secrets, `.env`, `local.settings.json`, generated ARM JSON, tokens, and browser auth state are never committed.

## Local development and quality strategy

Docker Compose provides application SQL Server, the Service Bus emulator and its separate SQL dependency, and Azurite. Application processes run on the host with trusted development HTTPS. Checked-in scripts perform readiness checks, migrations, startup guidance, and safe cleanup without embedding secrets.

Tests are layered: fast domain/application unit tests; SQL Server/Testcontainers integration and concurrency tests; .NET end-to-end service tests; bUnit component tests; Playwright cross-browser, accessibility, authentication, resilience, and observability journeys; and NBomber test-environment load tests. Test names describe behavior, and every phase has a focused verification gate before broader suites run.

## Delivery sequence

1. Bootstrap the repository, solution policy, contributor guide, documentation, and unprivileged CI.
2. Freeze executable API, event, telemetry, and schema contracts.
3. Establish workflow safeguards, repository-owned contracts, and the protected Azure `test` environment before material data features.
4. Implement SQL persistence and durable HTTP idempotency, then separately add Entra authorization and customer history.
5. Implement outbox publication, Inbox processing, settlement, and failure-window tests.
6. Make the local Docker/emulator stack and happy/failure paths reproducible.
7. Build Azure foundation Bicep, private networking, identities, RBAC, and SQL bootstrap.
8. Implement and deploy the two Flex Consumption Function Apps.
9. Containerize/deploy the API and establish the Blazor WASM hosting, typed-client, and authentication boundary.
10. Build the responsive frontend shell, dispatch-control design system, shared components, and accessibility baseline.
11. Implement the order lookup, creation, details, history, and status-tracking workflows against the deployed API.
12. Complete frontend resilience, cross-browser/accessibility evidence, browser telemetry, performance budgets, deployment, and rollback verification.
13. Add non-production TestSupport, Observability Lab, KQL, workbooks, alerts, and full E2E evidence.
14. Complete artifact promotion, security/supply-chain checks, load tests, and operational runbooks.
15. Complete restore, rollback, and production-readiness gates.

Each remaining phase must leave the repository buildable and include objective acceptance commands. It then receives three working days of independent development-environment verification selected for the technology/risk and one to two working days of QA-only testing in Azure `test`; evidence, defects, and re-test records are retained. Defects are fixed only from a fresh `feature/*` branch and re-promoted. Database changes use expand/migrate/contract; deployment never calls `Database.Migrate()` from application startup.

## Deferred execution inputs

The following are intentional phase gates, not unspecified architecture:

- GitHub owner/organization and repository visibility before creating `ordersApp` remotely.
- Azure tenant, subscription, approved region, naming suffix, budget, and resource owners before provisioning.
- Entra tenant policies, app-registration ownership, test identities, and production domain before authentication deployment.
- Alert recipients, retention/legal approval, and production support ownership before production readiness.

Codex must prompt the user when a gate is reached and must not invent these values. Planning and local implementation may proceed without them.

## Definition of success

The solution is complete only when the handoff's section 35 definition of done is evidenced: clean build and test suites, compatible migrations/contracts, reproducible local failure drills, secure immutable Azure deployments, least-privilege negative tests, correlated operational evidence, exercised alerts/runbooks, restore and rollback validation, and complete removal of TestSupport and synthetic credentials from production.
