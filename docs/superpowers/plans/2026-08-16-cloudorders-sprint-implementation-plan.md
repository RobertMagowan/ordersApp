# CloudOrders Sprint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` (inline) or `superpowers:subagent-driven-development` (one fresh worker per task) to execute this plan task-by-task. Sprint tasks use checkbox syntax and end with a verification gate.

**Goal:** Build a manually testable and deployable .NET 10/Azure CloudOrders system in short sprints, with every sprint leaving a runnable artifact and documented acceptance evidence.

**Architecture:** Standalone Blazor WebAssembly on Azure Static Web Apps Standard calls a conventional ASP.NET Core API on Azure Container Apps. The API commits Orders, IdempotencyRecords, and OutboxMessages atomically to Azure SQL; separate .NET 10 isolated Flex Consumption Functions publish the outbox to Service Bus and process messages through an Inbox transaction. Non-production TestSupport and observability tooling are isolated from production.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core, EF Core 10, SQL Server/Azure SQL, Azure Functions isolated worker, Azure Service Bus, Blazor WebAssembly, Bicep, Docker Compose, xUnit (current stable major, unless changed at Sprint 0), bUnit, Playwright, NBomber, OpenTelemetry/Azure Monitor, GitHub Actions with OIDC.

## Global Constraints

- Work from `C:\repos\OrderApp`; this directory is the repository root and default working directory.
- Use .NET 10 and stable C# 14; pin the installed stable SDK in `global.json` and do not enable preview language features.
- Product/namespace names are `CloudOrders`; the intended GitHub repository name is `ordersApp`.
- Git branches are `feature/*`/`agent/*` → `development` → `test` → `master`. Each protected-branch PR requires exactly one approving review; merges deploy to the matching GitHub environment.
- Because this is a single-developer repository, repository administrators may bypass the review requirement for their own PR. Required CI, source-branch, and conversation-resolution checks remain enforced.
- Sections 25–35 of `CLOUDORDERS_HANDOFF.md` are the authoritative version-1 contracts.
- Keep `CloudOrders.Api` free of direct Service Bus publishing; API writes Order + Outbox + Idempotency in one transaction.
- Preserve at-least-once messaging, stable `EventId`, insert-first Inbox idempotency, and explicit broker settlement.
- Do not run `Database.Migrate()` from application startup; migrations/security bootstrap run as deployment steps.
- Never commit secrets, `.env`, `local.settings.json`, generated ARM JSON, auth storage state, or real customer data.
- Every sprint is a sequence of focused commits; each meaningful red/green/refactor or packaging boundary is committed separately. The sprint ends with a manual test script, a machine-verifiable test command, and a deployable artifact or an explicit infrastructure gate.
- Prompt the user before using values not discoverable locally: GitHub owner/visibility, Azure tenant/subscription/region, production domain, Entra app registrations, alert recipients, or budget owners.

## Repository Map and File Ownership

Create/modify only the following responsibilities in the first implementation pass:

```text
CloudOrders.slnx, global.json, Directory.Build.props, Directory.Packages.props,
.editorconfig, .gitignore, AGENTS.md, README.md       repository policy
src/CloudOrders.Domain/                                  entities and invariants
src/CloudOrders.Application/                            use cases and ports
src/CloudOrders.Contracts/                              API/event DTOs
src/CloudOrders.Infrastructure/                         EF Core, SQL, messaging adapters
src/CloudOrders.Api/                                    HTTP host and auth
src/CloudOrders.Api.Client/                             generated/focused typed client
src/CloudOrders.OutboxPublisher/                        timer Function
src/CloudOrders.OrderProcessor/                         Service Bus Function
src/CloudOrders.Web/                                    standalone WASM UI
src/CloudOrders.TestSupport.Api/                        non-production controls only
tests/CloudOrders.UnitTests/                            fast domain/application tests
tests/CloudOrders.IntegrationTests/                     SQL/provider/concurrency tests
tests/CloudOrders.EndToEndTests/                        API/emulator service flows
tests/CloudOrders.Web.Tests/                            bUnit/component tests
tests/CloudOrders.Playwright/                           browser and observability tests
tests/CloudOrders.LoadTests/                            NBomber staging workloads
infra/                                                  Bicep source and env params
local/                                                  Compose, emulator config, scripts
ops/                                                    KQL, alerts, workbooks, runbooks
.github/workflows/                                      CI and deployment workflows
docs/                                                   ADRs, sprint evidence, runbooks
```

## Sprint 0 — Repository Bootstrap and Contributor Contract

**Outcome:** A clean checkout restores, builds an executable solution skeleton, and explains exactly how contributors work.

**Files:** Create `CloudOrders.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `AGENTS.md`, `README.md`, `src/CloudOrders.Domain/CloudOrders.Domain.csproj`, `src/CloudOrders.Application/CloudOrders.Application.csproj`, `src/CloudOrders.Contracts/CloudOrders.Contracts.csproj`, `src/CloudOrders.Infrastructure/CloudOrders.Infrastructure.csproj`, `src/CloudOrders.Api/CloudOrders.Api.csproj`, and matching test project files under `tests/`.

**Tasks:**

- [ ] Pin the installed stable .NET 10 SDK in `global.json`; set `TargetFramework` to `net10.0`, nullable/analyzers/deterministic builds in `Directory.Build.props`, and central versions in `Directory.Packages.props`.
- [ ] Add project references in dependency direction Domain → Application/Contracts → Infrastructure/API; tests reference only the production project under test.
- [ ] Write `AGENTS.md` with root path, sprint gate, structure, commands, naming, testing, commit/PR, security, and Azure decision-gate rules.
- [ ] Add a README quick start with `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`, and a statement that the application is bootstrapped but not yet deployed.
- [ ] Add unprivileged `.github/workflows/ci.yml` for restore, format check, Release build, and tests.
- [ ] Add the first failing architecture test that rejects a Domain reference to Infrastructure/API; make it pass with project references and a simple dependency checker.

**Manual test:** From `C:\repos\OrderApp`, run `dotnet restore`, `dotnet format --verify-no-changes`, `dotnet build -c Release`, and `dotnet test -c Release`. Expected: all commands succeed on a clean checkout.

**Sprint gate:** The solution builds, tests run, CI validates the same commands, and `AGENTS.md` is present. Commit `chore: bootstrap CloudOrders repository`.

**Natural commit checkpoints:** repository policy/scaffolding; failing architecture test; green policy tests and CI workflow.

## Sprint 0.5 — DevOps Promotion Foundation

**Outcome:** Every code promotion is review-gated and deployable through the `development`, `test`, and `production` GitHub environments.

**Tasks:**

- [ ] Protect `development`, `test`, and `master` with pull requests, exactly one approval, required CI and promotion-policy checks, conversation resolution, and no force-push/deletion.
- [ ] Enforce source branches: feature/agent branches to `development`, `development` to `test`, and `test` to `master`.
- [ ] Configure environment branch restrictions and deployment concurrency; keep environment reviewers empty so the single PR approval remains the only approval gate.
- [ ] Add Azure OIDC workflow scaffolding and document the required environment variables/secrets. Keep deployment disabled until Azure resources and federated credentials exist.
- [ ] Add CODEOWNERS, pull request template, and rename the default branch from `main` to `master`.

**Manual test:** Open a PR with an invalid source branch and verify the promotion-policy check fails; open valid promotion PRs and verify exactly one approval is required. Confirm a merge starts the matching environment workflow.

**Deploy gate:** GitHub branch protections, environments, and deployment workflow are configured. Azure deployment becomes active when the approved subscription, tenant, region, resource group, and OIDC identity are supplied.

**Natural commit checkpoints:** branch rename; repository policy/workflows; branch/environment protection; verification and evidence.

## Sprint 0.6 — MVP Azure Container Deployment

**Outcome:** The current API vertical slice can be built as a non-root .NET 10 container and deployed to Azure Container Apps through the protected environment workflow.

**Files:** `infra/main.bicep`, `infra/environments/development.bicepparam`, `src/CloudOrders.Api/Dockerfile`, `.dockerignore`, and `.github/workflows/deploy.yml`.

**Tasks:**

- [ ] Provision Log Analytics, Basic ACR, Container Apps managed environment, and an externally reachable API Container App with HTTPS and liveness probing.
- [ ] Use a system-assigned Container App identity with `AcrPull`; keep ACR admin credentials disabled.
- [ ] Deploy a public bootstrap image on the first pass, then build/push the commit-SHA API image and redeploy it through the same Bicep workflow.
- [ ] Authenticate GitHub Actions to Azure with OIDC and environment-scoped values; keep the workflow disabled until federated credentials and resource-group values are configured.
- [ ] Smoke-test `/health/live` after deployment and retain the deployment URL in the workflow summary.

**Manual test:** Enable the development environment, merge a verified PR into `development`, confirm the workflow provisions the resources, publishes the image, and returns HTTP 200 from `/health/live`.

**Deploy gate:** `az bicep build`, Docker build/run, and the GitHub deployment workflow are green. Azure what-if and production resource creation remain decision gates until subscription, tenant, region, and budget ownership are confirmed.

**Natural commit checkpoints:** Bicep foundation; container packaging; OIDC deployment workflow; smoke-test/documentation evidence.

## Sprint 1 — Domain, Contracts, and API Vertical Slice

**Outcome:** A developer can create and read an order locally through a documented API, with no broker dependency.

**Files:** Create domain order/status/value types; application `CreateOrder`/`GetOrder` handlers and ports; contracts `OrderResponse`, `CreateOrderRequest`, `ProblemDetails` mapping, and `OrderCreatedIntegrationEventV1`; API endpoints, validators, OpenAPI setup, `/health/live`, and `/health/ready`; unit and API integration tests.

**Interfaces:** `CreateOrderHandler.Handle(CreateOrderCommand, CancellationToken) -> Result<Order>`; `IOrderRepository`; `IOutboxWriter`; `POST /api/v1/orders`; `GET /api/v1/orders/{orderId}`.

**Tasks:**

- [ ] Write failing tests for `Pending` creation, only `Pending -> Processing`, quantity 1–100, canonical uppercase references, and invalid identifiers.
- [ ] Implement the domain aggregate and explicit response/event DTOs; do not serialize EF entities.
- [ ] Write API tests for 201 creation, 200/404 reads, validation 400, and Problem Details shape.
- [ ] Implement minimal API/controller endpoints with `CreatedAtAction`, UTC timestamps, and no Service Bus registration.
- [ ] Add SQL-backed persistence only after the in-memory application tests pass; keep liveness independent of SQL and readiness SQL-aware.

**Manual test:** Run the API locally, call `POST /api/v1/orders` with a valid JSON body, then `GET` the returned ID; verify status `pending`, UTC timestamps, OpenAPI output, `/health/live` 200, and `/health/ready` behavior.

**Deploy gate:** Produce a versioned API container image locally with `dotnet publish`/Docker; deploy only to the local Compose SQL environment until Sprint 4 creates Azure resources. Commit `feat: add order API vertical slice`.

**Natural commit checkpoints:** failing domain tests; green domain/contracts; application handler tests and implementation; API integration tests and host wiring; publish/manual evidence.

## Sprint 2 — SQL Schema and Durable HTTP Idempotency

**Outcome:** Repeated or concurrent POSTs cannot create duplicate orders, and the API is deployable against SQL Server.

**Files:** EF configurations and `Orders`, `OutboxMessages`, `IdempotencyRecords` models; migrations; `Idempotency-Key` middleware/handler; SQL integration tests using Testcontainers; API client examples.

**Interfaces:** `IIdempotencyStore.TryGetAsync(subjectId, key)`; `IdempotencyRecord`; `POST` requires a UUID `Idempotency-Key`; responses are 201 first-use, 200 exact replay with `Idempotency-Replayed: true`, 409 payload conflict.

**Tasks:**

- [ ] Write failing SQL tests for first request, exact replay, same-key/different-payload conflict, and two concurrent requests.
- [ ] Add the schema/indexes and one transaction covering Order + Outbox row placeholder + IdempotencyRecord.
- [ ] Implement canonical hash `SHA-256(v1|subjectId|customerReference|productSku|quantity)` and recheck authorization on replay.
- [ ] Add migration commands that target an explicit connection string and never run from web startup.
- [ ] Add request body size, rate-limit, timeout, and unknown-member validation tests.

**Manual test:** Start SQL with `docker compose -f local/compose.yml up -d cloudorders-sql`; apply the migration command; submit the same request twice with the same key and once with changed quantity; verify one row, replay 200, and conflict 409.

**Deploy gate:** Build the API image and run it against the local SQL container with no Service Bus setting. Commit `feat: add SQL persistence and POST idempotency`.

**Natural commit checkpoints:** failing schema/idempotency tests; migration and persistence implementation; replay/conflict behavior; API verification.

## Sprint 3 — Outbox Publisher and Inbox Processor

**Outcome:** A local order moves from `Pending` to `Processing` through Service Bus with duplicate-safe, failure-aware processing.

**Files:** `OutboxMessage`/`InboxMessage` persistence; `OutboxDispatcher`; publisher timer Function; processor Service Bus Function; emulator config; messaging/unit/integration tests.

**Interfaces:** `OutboxDispatcher.DrainAsync(CancellationToken)`; `OrderProcessor.ProcessAsync(ServiceBusReceivedMessage, CancellationToken)`; `OrderCreatedIntegrationEventV1`; broker properties `EventId`, `messageType`, `messageVersion`, `traceparent`.

**Tasks:**

- [ ] Write failing tests for bounded outbox chunks, broker-size batching, send-then-mark ordering, broker failure, and crash-after-send.
- [ ] Implement publisher with `ProcessedAt IS NULL`, stored JSON reuse, bounded chunks, and explicit pending-row preservation.
- [ ] Write failing tests for insert-first Inbox claim, duplicate delivery, invalid version/payload, SQL rollback, and settlement failure.
- [ ] Implement one DI scope/DbContext/transaction per processor message with explicit Complete/Abandon/DeadLetter decisions.
- [ ] Add structured fields for `MessageId`, `EventId`, `OrderId`, and `DeliveryCount` without payload secrets.

**Manual test:** Start the Service Bus emulator and both Functions; create an order; observe outbox pending → published → processed and `Pending → Processing`. Stop the broker, create another order, restart it, and verify recovery. Inject one duplicate and verify one Inbox row/business transition.

**Deploy gate:** Package both isolated Functions locally with `dotnet publish`; local emulator happy path and failure drills pass. Commit `feat: add transactional outbox and inbox processing`.

**Natural commit checkpoints:** failing publisher tests; green publisher; failing processor tests; green processor and settlement; local failure-drill evidence.

## Sprint 4 — Reproducible Local Platform

**Outcome:** A clean checkout can start every local dependency and host with one documented/manual sequence.

**Files:** `local/compose.yml`, `local/servicebus/Config.json`, `.env.example`, readiness/migration scripts, Function `local.settings.json.example` files, local README section, and `CloudOrders.EndToEndTests`.

**Tasks:**

- [ ] Pin SQL Server, Service Bus emulator, emulator SQL dependency, and Azurite images; use health checks, named volumes, loopback-only ports, and `MSSQL_SA_PASSWORD`.
- [ ] Configure `orders` queue lock, TTL, duplicate detection, max delivery count, and DLQ behavior.
- [ ] Add a PowerShell readiness script that prints URLs and never prints secret values.
- [ ] Add service-level tests for healthy, broker outage/recovery, duplicate, transient processor retry, poison/DLQ, and replay scenarios.
- [ ] Add safe cleanup that does not delete named volumes unless the operator explicitly asks.

**Manual test:** Follow README from a clean checkout: trust HTTPS certificate, start Compose, wait for health, apply migrations, start API/Functions/Web, create an order, and run the local E2E smoke test.

**Deploy gate:** Local stack is reproducible without undocumented steps; evidence is stored under `docs/evidence/sprint-4/`. Commit `test: make local order flow reproducible`.

**Natural commit checkpoints:** Compose health/config; readiness and migration scripts; service-level tests; manual evidence and documentation.

## Sprint 5 — Azure Foundation and Identity Graph

**Outcome:** Development Azure resources exist through reviewed Bicep and private connectivity/RBAC is testable before application deployment.

**Decision gate:** Prompt for Azure tenant, subscription, approved region, naming suffix, budget alert owner, and resource tags before any deployment.

**Files:** `infra/main.bicep`, `foundation.bicep`, `modules/{network,private-dns,monitoring,sql,service-bus,acr,identities,role-assignments}.bicep`, `infra/environments/dev.bicepparam`, SQL bootstrap/migration project, and what-if evidence.

**Tasks:**

- [ ] Verify current stable Bicep API versions and regional support for SQL, Service Bus, Storage, ACR, Log Analytics, and managed identities.
- [ ] Add resource group, tags, non-overlapping VNet, Container Apps `/27+`, Flex integration `/26`, private-endpoint `/27+`, private DNS, SQL, Service Bus, ACR, Log Analytics, and Application Insights.
- [ ] Add environment-specific managed identities and least-privilege Azure roles; do not assign runtime `Owner`/`Contributor`/`db_owner`.
- [ ] Add a migration/security bootstrap that creates contained SQL users and roles outside application startup.
- [ ] Run `az bicep build`, `az deployment sub validate`, and `az deployment sub what-if`; review destructive changes manually.

**Manual test:** From an approved private runner, resolve private DNS and prove SQL login, Service Bus send/receive, Storage access, and ACR image pull with the intended identities; prove an unauthorized identity fails.

**Deploy gate:** Development foundation is deployed with clean what-if and least-privilege evidence. Commit `infra: add development Azure foundation`.

**Natural commit checkpoints:** Bicep module groups; environment parameters; identity/RBAC; SQL bootstrap; what-if and private-connectivity evidence.

## Sprint 6 — Azure Functions Hosting

**Outcome:** Publisher and processor run in two .NET 10 isolated Linux Flex Consumption apps with managed-identity access and rollbackable packages.

**Files:** Function host Bicep modules, `host.json`, `Program.cs`, app settings templates, package manifest scripts, telemetry configuration, and `.github/workflows/deploy-functions.yml`.

**Tasks:**

- [ ] Recheck current Flex/.NET 10/runtime/VNet support for the approved region.
- [ ] Configure separate storage accounts, identity-based host storage, Service Bus sender/receiver roles, SQL roles, Application Insights, timer schedule, batch/lock/timeout settings, and no public inbound product endpoint.
- [ ] Produce deterministic zip packages and SHA-256 manifests; deploy an immutable package and record the manifest.
- [ ] Add smoke commands for timer invocation, Service Bus processing, telemetry, and rollback to the previous package.

**Manual test:** Create an order through the local/dev API, verify both Functions process it with managed identity, inspect structured telemetry, and redeploy the previous package to prove rollback.

**Deploy gate:** Functions process a test order in Azure without secrets or SQL passwords. Commit `feat: deploy isolated Functions on Flex Consumption`.

**Natural commit checkpoints:** host/runtime settings; publisher deployment; processor deployment; identity/telemetry; rollback evidence.

## Sprint 7 — API Container App and Blazor WebAssembly

**Outcome:** An authenticated browser user can create, find, and track an order through the linked Static Web Apps `/api` path.

**Decision gate:** Prompt for Entra tenant/app-registration ownership, non-production test users, approved production domain, and exact GitHub environment names before protected deployment.

**Files:** API Dockerfile/`.dockerignore`; Container App Bicep; `src/CloudOrders.Web`; `src/CloudOrders.Api.Client`; Static Web Apps Bicep/workflow; bUnit tests; frontend auth/configuration.

**Tasks:**

- [ ] Build a patched, non-root, deterministic .NET 10 API image with immutable commit-SHA tagging and vulnerability scanning.
- [ ] Add Entra authorization-code/PKCE WASM authentication, typed API client, bearer/trace/idempotency handlers, routes, Problem Details, loading/error/access-denied states, and `Received → Processing` UI.
- [ ] Add bUnit tests for validation, focus, idempotent submission, authorization, cancellation, error mapping, and reduced-motion markup.
- [ ] Configure Static Web Apps Standard linked to the API Container App, exact headers/CSP/CORS, SPA fallback, no-cache index, and immutable assets.
- [ ] Add Playwright smoke for sign-in, create, refresh, find, and status transition.

**Manual test:** Sign in with a non-production test user, create an order in the browser, refresh, locate it in history, and verify the UI reaches `Processing` through the same-origin `/api` path.

**Deploy gate:** API Container App and Static Web Apps deploy from immutable artifacts; browser smoke and bUnit pass. Commit `feat: add authenticated web experience`.

**Natural commit checkpoints:** API image/health; typed client/auth; UI route slices; Static Web Apps edge; browser/accessibility evidence.

## Sprint 8 — TestSupport and Observability Evidence

**Outcome:** Non-production reliability scenarios are safely controllable and produce correlated UI, SQL, broker, and Azure Monitor evidence.

**Files:** `CloudOrders.TestSupport.Api`, `testsupport.ScenarioLeases` migration, fault policies, Observability Lab components, Playwright fixtures/specs/reporters, `ops/kql`, workbook/alert Bicep, and production exclusion tests.

**Tasks:**

- [ ] Implement bounded leases, allowlisted faults, automatic expiry, cleanup, TestOperator authorization, rate limits, audit events, and DLQ replay.
- [ ] Add W3C trace/test-run/scenario/order/event propagation through API, Outbox, Service Bus, Functions, and browser telemetry.
- [ ] Add required lifecycle events and KQL for healthy, retry, duplicate, poison/DLQ, replay, and telemetry-silence scenarios.
- [ ] Add Playwright projects for browser/accessibility/auth, local smoke, and serial non-production observability; redact auth state and secrets.
- [ ] Add tests proving TestSupport, fault injection, synthetic identities, and diagnostic routes are absent from production templates/workflows.

**Manual test:** Run one healthy and one faulted staging scenario; verify UI state, database state, broker settlement/DLQ, trace IDs, KQL results, cleanup, and alert behavior agree.

**Deploy gate:** Every supported scenario has retained evidence and safe cleanup. Commit `test: add non-production observability lab`.

**Natural commit checkpoints:** TestSupport lease/safety controls; fault policies; telemetry propagation/KQL; Playwright scenarios; production exclusion tests.

## Sprint 9 — CI/CD, Promotion, and Operations

**Outcome:** Pull requests produce trusted evidence; protected promotion deploys the same immutable artifacts through environments.

**Decision gate:** Prompt for GitHub owner/organization, repository visibility, protected-environment reviewers, deployment identities, and alert/action-group owners before remote setup.

**Files:** `.github/workflows/{ci,infrastructure,deploy-apps,e2e,scheduled-e2e,load}.yml`, release manifest generator, `ops/runbooks/*`, budgets/alerts Bicep, Dependabot/security configuration, and README deployment docs.

**Tasks:**

- [ ] CI: restore, format, build, unit/integration/contract/bUnit, OpenAPI diff, container/IaC/dependency/secret scans, and local Playwright subset.
- [ ] Infrastructure workflow: OIDC login, Bicep build/validate/what-if, protected deployment, concurrency, and evidence retention.
- [ ] Application workflow: migrations/security bootstrap, API image push, Function packages, WASM artifact, smoke, protected E2E, and release manifest.
- [ ] Add rollback/runbooks for DLQ replay, stuck outbox, failed migration, identity/network failure, telemetry silence, restore, and release rollback.
- [ ] Add NBomber staging workloads with cost guardrails and publish results/metrics.

**Manual test:** Open a PR and verify unprivileged checks; merge to a protected development environment and verify immutable artifact promotion, smoke, E2E, telemetry, and rollback evidence.

**Deploy gate:** The GitHub repository `ordersApp` can build and promote without long-lived Azure secrets. Commit `ci: add protected artifact promotion`.

**Natural commit checkpoints:** CI/security checks; infrastructure OIDC workflow; application artifact workflow; runbooks/alerts; staging load evidence.

## Sprint 10 — Production Readiness

**Outcome:** The system is supportable, secure, cost-controlled, and ready for a reviewed production promotion.

**Tasks:**

- [ ] Run the complete Release, SQL migration-upgrade, contract, unit, integration, bUnit, Playwright, accessibility, security, load, restore, and rollback matrix from a clean checkout.
- [ ] Verify private networking, TLS, API edge/linking, CORS, rate limits, request limits, identity negative tests, backup/retention, budgets, quotas, tags, and region support.
- [ ] Remove bootstrap firewall rules, credentials, unused resources, development fault hooks, TestSupport production paths, and obsolete documentation.
- [ ] Have a second operator execute the runbooks and record evidence under `docs/evidence/sprint-10/`.
- [ ] Prompt for final production approval and domain/owner confirmation; do not create production resources without it.

**Deploy gate:** Handoff section 35 is fully evidenced and the production approval is recorded. Commit `release: complete CloudOrders version one readiness`.

**Natural commit checkpoints:** final test matrix; security/network review; restore/rollback evidence; production approval record.

## Cross-sprint verification commands

Run from `C:\repos\OrderApp` unless a sprint says otherwise:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test --configuration Release
dotnet tool restore
docker compose -f local/compose.yml config
az bicep build --file infra/main.bicep
```

PowerShell execution policy may block `npm.ps1`; use `npm.cmd` for Playwright commands when necessary. Never treat a green unit test run as a substitute for the sprint's manual flow or deploy gate.

## Plan self-review

- **Spec coverage:** repository policy (S0), application contracts (S1–S2), outbox/inbox (S3), local reproducibility (S4), Azure foundation/RBAC (S5), Flex Functions (S6), web/API edge (S7), TestSupport/observability (S8), CI/CD/operations (S9), and production definition of done (S10) are each assigned.
- **Placeholder scan:** no `TBD`, `TODO`, `FIXME`, “implement later”, or unowned “add appropriate handling” steps are used; user-owned values are explicit decision gates.
- **Type/interface consistency:** `OrderCreatedIntegrationEventV1`, `OutboxDispatcher.DrainAsync(CancellationToken)`, `OrderProcessor.ProcessAsync(ServiceBusReceivedMessage, CancellationToken)`, `IIdempotencyStore`, and `POST /api/v1/orders` retain the same names and semantics across sprints.
- **Manual deployability:** S0–S4 are local-deployable; S5 provisions the development platform; S6–S9 deploy each cloud workload incrementally; S10 is the production gate.
