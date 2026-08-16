# CloudOrders Sprint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` (inline) or `superpowers:subagent-driven-development` (one fresh worker per task) to execute this plan task-by-task. Sprint tasks use checkbox syntax and end with a verification gate.

**Goal:** Build a manually testable and deployable .NET 10/Azure CloudOrders system in short sprints, with every sprint leaving a runnable artifact and documented acceptance evidence.

**Architecture:** Standalone Blazor WebAssembly on Azure Static Web Apps Standard calls a conventional ASP.NET Core API on Azure Container Apps. The API commits Orders, IdempotencyRecords, and OutboxMessages atomically to Azure SQL; separate .NET 10 isolated Flex Consumption Functions publish the outbox to Service Bus and process messages through an Inbox transaction. Non-production TestSupport and observability tooling are isolated from production.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core, EF Core 10, SQL Server/Azure SQL, Azure Functions isolated worker, Azure Service Bus, Blazor WebAssembly, Bicep, Docker Compose, xUnit (current stable major, unless changed at Sprint 0), bUnit, Playwright, NBomber, OpenTelemetry/Azure Monitor, GitHub Actions with OIDC.

## Global Constraints

- Work from `C:\repos\OrderApp`; this directory is the repository root and default working directory.
- Use .NET 10 and stable C# 14; pin the installed stable SDK in `global.json` and do not enable preview language features.
- Product/namespace names are `CloudOrders`; the intended GitHub repository name is `ordersApp`.
- Git branches are `feature/*` → `development` → `test` → `master`; all new feature branches use the `feature/` prefix. Protected-branch PRs require the configured checks and conversation resolution; this single-developer repository requires zero independent approvals. Merges deploy to the matching GitHub environment.
- The GitHub/Azure environment named `test` is the staging-equivalent release environment; use `test` consistently in branch, workflow, parameter-file, evidence, and cost language.
- Because this is a single-developer repository, protected branches require zero independent approvals; the administrator remains responsible for reviewing each PR. Required CI, source-branch, and conversation-resolution checks remain enforced.
- Section 19 of the source handoff at `C:\Users\admin\Documents\Codex\2026-08-16\referenced-chatgpt-conversation-this-is-an\outputs\CLOUDORDERS_HANDOFF.md` is the frontend design authority, and sections 25–35 are the authoritative version-1 contracts. Sprint 2 must add both to a repository-owned contract pack so future execution does not depend on that machine-local path.
- Keep `CloudOrders.Api` free of direct Service Bus publishing; API writes Order + Outbox + Idempotency in one transaction.
- Preserve at-least-once messaging, stable `EventId`, insert-first Inbox idempotency, and explicit broker settlement.
- Do not run `Database.Migrate()` from application startup; migrations/security bootstrap run as deployment steps.
- Never commit secrets, `.env`, `local.settings.json`, generated ARM JSON, auth storage state, or real customer data.
- Every sprint is a sequence of focused commits; each meaningful red/green/refactor or packaging boundary is committed separately. The sprint ends with a manual test script, a machine-verifiable test command, and a deployable artifact or an explicit infrastructure gate.
- Prompt the user before using values not discoverable locally: GitHub owner/visibility, Azure tenant/subscription/region, production domain, Entra app registrations, alert recipients, or budget owners.

## Delivery Status and Effort Model

Estimates assume one focused developer and include implementation, automated tests, manual verification, review corrections, Azure development deployment, and evidence capture. They exclude delays waiting for external access, approvals, DNS, quota, or Azure incidents.

| Sprint | Delivery | Status | Estimated effort |
|---|---|---|---:|
| 0 | Repository bootstrap | Baseline delivered | 1–2 days |
| 0.5 | DevOps promotion foundation | Baseline delivered | 2–3 days |
| 0.6 | MVP Azure container deployment and AVM hardening | Baseline delivered | 3–5 days |
| 1 | Domain, contracts, and API vertical slice | Baseline delivered to development | 4–6 days |
| 2 | SQL schema, API authorization, and durable HTTP idempotency | Next | 8–12 days |
| 3 | Outbox, inbox, Service Bus, and development Functions | Planned | 7–10 days |
| 4 | Reproducible local platform | Planned | 3–5 days |
| 5 | Azure security and network foundation | Planned | 8–12 days |
| 6 | Production-grade Azure Functions hosting | Planned | 4–6 days |
| 7 | Web delivery and authentication foundation | Planned | 4–6 days |
| 8 | Frontend shell and design system | Planned | 4–6 days |
| 9 | Order workflows | Planned | 5–8 days |
| 10 | Frontend quality and release integration | Planned | 4–6 days |
| 11 | TestSupport and observability evidence | Planned | 7–10 days |
| 12 | CI/CD, promotion, and operations | Planned | 7–10 days |
| 13 | Production readiness | Planned | 5–8 days |

The revised roadmap contains 16 sprint phases: three foundation phases plus Sprints 1–13. Total estimated effort is 76–115 working days; after the delivered work through Sprint 1, approximately 66–99 working days remain. Historical checklists remain unchecked as acceptance-audit items; Sprint 2 starts by evidencing each delivered item or carrying a concrete correction into its first commits.

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
tests/CloudOrders.LoadTests/                            NBomber test-environment workloads
infra/                                                  Bicep source and env params
local/                                                  Compose, emulator config, scripts
ops/                                                    KQL, alerts, workbooks, runbooks
.github/workflows/                                      CI and deployment workflows
docs/                                                   ADRs, sprint evidence, runbooks
```

## Sprint 0 — Repository Bootstrap and Contributor Contract

**Estimated effort:** 1–2 working days.

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

**Deploy gate:** This pre-application foundation has no Azure runtime to deploy; the solution builds, tests run, CI validates the same commands, and `AGENTS.md` is present. Commit `chore: bootstrap CloudOrders repository`.

**Natural commit checkpoints:** repository policy/scaffolding; failing architecture test; green policy tests and CI workflow.

## Sprint 0.5 — DevOps Promotion Foundation

**Estimated effort:** 2–3 working days.

**Outcome:** Every code promotion is review-gated and deployable through the `development`, `test`, and `production` GitHub environments.

**Tasks:**

- [ ] Protect `development`, `test`, and `master` with pull requests, zero required approvals, required CI and promotion-policy checks, conversation resolution, and no force-push/deletion.
- [ ] Enforce source branches: feature/agent branches to `development`, `development` to `test`, and `test` to `master`.
- [ ] Configure environment branch restrictions and deployment concurrency; keep environment reviewers empty because this single-developer repository has no independent PR approval gate.
- [ ] Add Azure OIDC workflow scaffolding and document the required environment variables/secrets. Keep deployment disabled until Azure resources and federated credentials exist.
- [ ] Add CODEOWNERS, pull request template, and rename the default branch from `main` to `master`.

**Manual test:** Open a PR with an invalid source branch and verify the promotion-policy check fails; open valid promotion PRs and verify the required checks and conversation resolution gate the merge without requiring an independent approval. Confirm a merge starts the matching environment workflow.

**Deploy gate:** GitHub branch protections, environments, and deployment workflow are configured. Azure deployment becomes active when the approved subscription, tenant, region, resource group, and OIDC identity are supplied.

**Natural commit checkpoints:** branch rename; repository policy/workflows; branch/environment protection; verification and evidence.

## Sprint 0.6 — MVP Azure Container Deployment

**Estimated effort:** 3–5 working days.

**Outcome:** The current API vertical slice can be built as a non-root .NET 10 container and deployed to Azure Container Apps through the protected environment workflow.

**Files:** `infra/main.bicep`, `infra/environments/development.bicepparam`, `src/CloudOrders.Api/Dockerfile`, `.dockerignore`, and `.github/workflows/deploy.yml`.

**Tasks:**

- [ ] Provision Log Analytics, Basic ACR, Container Apps managed environment, and an externally reachable API Container App with HTTPS and liveness probing.
- [ ] Use a system-assigned Container App identity with `AcrPull`; keep ACR admin credentials disabled.
- [ ] Deploy a public bootstrap image on the first pass, then build/push the commit-SHA API image and redeploy it through the same Bicep workflow.
- [ ] Authenticate GitHub Actions to Azure with OIDC and environment-scoped values; keep the workflow disabled until federated credentials and resource-group values are configured.
- [ ] Smoke-test `/health/live` after deployment and retain the deployment URL in the workflow summary.

**Manual test:** Enable the development environment, merge a verified PR into `development`, confirm the workflow provisions the resources, publishes the image, and returns HTTP 200 from `/health/live`.

**Deploy gate:** `az bicep build`, Docker build/run, and the GitHub development deployment workflow are green. Test/production what-if and resource creation remain decision gates until their budget, alert, and production ownership values are confirmed.

**Natural commit checkpoints:** Bicep foundation; container packaging; OIDC deployment workflow; smoke-test/documentation evidence.

## Sprint 1 — Domain, Contracts, and API Vertical Slice

**Estimated effort:** 4–6 working days.

**Outcome:** A developer can create and read an order locally and from the deployed development API, with no broker dependency.

**Files:** Create domain order/status/value types; application `CreateOrder`/`GetOrder` handlers and ports; contracts `OrderResponse`, `CreateOrderRequest`, `ProblemDetails` mapping, and `OrderCreatedIntegrationEventV1`; API endpoints, validators, OpenAPI setup, `/health/live`, and `/health/ready`; unit and API integration tests.

**Interfaces:** `CreateOrderHandler.Handle(CreateOrderCommand, CancellationToken) -> Result<Order>`; `IOrderRepository`; `IOutboxWriter`; `POST /api/v1/orders`; `GET /api/v1/orders/{orderId}`.

**Tasks:**

- [ ] Write failing tests for `Pending` creation, only `Pending -> Processing`, quantity 1–100, canonical uppercase references, and invalid identifiers.
- [ ] Implement the domain aggregate and explicit response/event DTOs; do not serialize EF entities.
- [ ] Write API tests for 201 creation, 200/404 reads, validation 400, and Problem Details shape.
- [ ] Implement minimal API/controller endpoints with `CreatedAtAction`, UTC timestamps, and no Service Bus registration.
- [ ] Keep Sprint 1 persistence in-memory; record SQL persistence, SQL-aware readiness, and atomic Order/Outbox/Idempotency storage as the first Sprint 2 increment.

**Manual test:** Run the API locally, call `POST /api/v1/orders` with a valid JSON body, then `GET` the returned ID; repeat the POST/GET journey against the deployed development API. Verify status `pending`, UTC timestamps, OpenAPI output, `/health/live` 200, and `/health/ready` behavior, and retain the Azure request/response evidence without customer-sensitive data.

**Deploy gate:** Publish the immutable API image through the protected `development` workflow, smoke-test `/health/live` plus deployed POST/GET behavior from Azure Container Apps, and retain the workflow URL as evidence. Commit `feat: add order API vertical slice`.

**Natural commit checkpoints:** failing domain tests; green domain/contracts; application handler tests and implementation; API integration tests and host wiring; publish/manual evidence.

## Sprint 2 — SQL Schema, API Authorization, and Durable HTTP Idempotency

**Estimated effort:** 8–12 working days.

**Outcome:** Repeated or concurrent POSTs cannot create duplicate orders, and the API is deployable against SQL Server.

**Decision gate:** Confirm ownership of the development `CloudOrders-Api-development` Entra registration, delegated scopes, claims/customer-scope source, and non-production API test identity before enabling Azure authorization.

**Files:** EF configurations and `Orders`, `OutboxMessages`, `IdempotencyRecords` models; migrations; API bearer authorization policies; `Idempotency-Key` middleware/handler; customer-history query/cursor contract; SQL integration tests using Testcontainers; initial SQL service in `local/compose.yml`; API client examples; focused Azure SQL/identity Bicep modules; deployment migration step.

**Interfaces:** `IIdempotencyStore.TryGetAsync(subjectId, key)`; `IdempotencyRecord`; `POST` requires a UUID `Idempotency-Key`; responses are 201 first-use, 200 exact replay with `Idempotency-Replayed: true`, 409 payload conflict; `GET /api/v1/customers/{customerReference}/orders` uses stable newest-first cursor pagination.

**Tasks:**

- [ ] Write failing SQL tests for first request, exact replay, same-key/different-payload conflict, and two concurrent requests.
- [ ] Audit the delivered Sprints 0–1 checklists against git, CI, and Azure evidence; mark evidenced items complete and fix or explicitly carry every gap before accepting new Sprint 2 behavior.
- [ ] Close the live deployment-workflow findings before adding data: reject manual dispatch from branches other than `development`, `test`, or `master`; remove `curl --insecure`; write the endpoint/release to `$GITHUB_STEP_SUMMARY`; and make the public bootstrap image a one-time creation path so routine releases never replace a healthy API with the placeholder image.
- [ ] Add a repository-owned version-1 contract pack under `docs/contracts/` that restates the handoff's frontend authority in section 19 and version-1 contracts in sections 25–35, then maps each requirement to the implementing projects, tests, and sprint gates, removing execution dependence on the machine-local source before Sprint 8 begins.
- [ ] Add the schema/indexes and one transaction covering Order + Outbox row placeholder + IdempotencyRecord.
- [ ] Add API Entra bearer validation, `CloudOrders.Orders.Read`/`CloudOrders.Orders.Write` policies, authorized customer-scope derivation, and cross-customer `404` behavior before using the authenticated subject in the idempotency key.
- [ ] Implement canonical hash `SHA-256(v1|subjectId|customerReference|productSku|quantity)` and recheck authorization on replay.
- [ ] Implement authorized customer-history queries with stable `(CreatedAtUtc, Id)` ordering, opaque cursor state, page size 1–100, and tests for paging, bounds, and cross-customer non-disclosure.
- [ ] Add migration commands that target an explicit connection string and never run from web startup.
- [ ] Add request body size, rate-limit, timeout, and unknown-member validation tests.
- [ ] Add the initial pinned SQL service and health check to `local/compose.yml` so the documented local migration/idempotency flow is executable before Sprint 4 hardens the complete stack.
- [ ] Provision the development Azure SQL server/database, assign the API managed identity the minimum database role, and run migrations through a deployment identity before updating the API revision. Until the Sprint 5 Azure-native deployment Job is available, any bootstrap firewall rule must have an owner/expiry and be removed in Sprint 5.

**Manual test:** Run replay/conflict/concurrency against local SQL, then exercise Azure positive authorization, cross-customer `404`, two cursor pages, first-use, exact replay, conflict, concurrent submission, and replay after API restart. Verify exactly one Order, one Outbox row, and one IdempotencyRecord for the idempotent operation.

**Deploy gate:** The protected workflow validates Bicep, applies the reviewed migration, deploys the immutable API image to development, and passes live/readiness, authorization-negative, customer-history pagination, persistence, and idempotency smoke tests in Azure. Commit `feat: add SQL persistence and POST idempotency`.

**Natural commit checkpoints:** failing schema/idempotency tests; migration and persistence implementation; replay/conflict behavior; API verification.

## Sprint 3 — Outbox Publisher and Inbox Processor

**Estimated effort:** 7–10 working days.

**Outcome:** An order moves from `Pending` to `Processing` locally and in Azure development through Service Bus with duplicate-safe, failure-aware processing.

**Files:** `OutboxMessage`/`InboxMessage` persistence; `OutboxDispatcher`; publisher timer Function; processor Service Bus Function; Service Bus emulator/Azurite additions to `local/compose.yml`; emulator config; messaging/unit/integration tests; development Service Bus, storage, identities, minimum Function host Bicep; Function package workflow.

**Interfaces:** `OutboxDispatcher.DrainAsync(CancellationToken)`; `OrderProcessor.ProcessAsync(ServiceBusReceivedMessage, CancellationToken)`; `OrderCreatedIntegrationEventV1`; broker properties `EventId`, `messageType`, `messageVersion`, `traceparent`.

**Tasks:**

- [ ] Write failing tests for bounded outbox chunks, broker-size batching, send-then-mark ordering, broker failure, and crash-after-send.
- [ ] Implement publisher with `ProcessedAt IS NULL`, stored JSON reuse, bounded chunks, and explicit pending-row preservation.
- [ ] Write failing tests for insert-first Inbox claim, duplicate delivery, invalid version/payload, SQL rollback, and settlement failure.
- [ ] Implement one DI scope/DbContext/transaction per processor message with explicit Complete/Abandon/DeadLetter decisions.
- [ ] Add structured fields for `MessageId`, `EventId`, `OrderId`, and `DeliveryCount` without payload secrets.
- [ ] Extend the Sprint 2 Compose file with pinned Service Bus emulator, emulator SQL dependency, and Azurite services sufficient to execute the local publisher/processor flow; Sprint 4 adds full readiness, safe cleanup, and documentation.
- [ ] Provision the development Service Bus queue and minimum isolated Function hosts with managed identities; deploy immutable packages and run the Azure happy path, duplicate, outage/recovery, and poison-message smoke set. Record any temporary public data-service access with owner/expiry for removal in Sprint 5.

**Manual test:** Run the local emulator flow, then create an order through the development API and observe Azure SQL outbox pending → published, Service Bus delivery, Inbox insertion, and `Pending → Processing`. Exercise one duplicate and one controlled poison message and verify settlement/DLQ behavior.

**Deploy gate:** Package both isolated Functions deterministically, deploy them and the API to development, and retain Azure evidence for the happy path and bounded failure drills. Commit `feat: add transactional outbox and inbox processing`.

**Natural commit checkpoints:** failing publisher tests; green publisher; failing processor tests; green processor and settlement; local failure-drill evidence.

## Sprint 4 — Reproducible Local Platform

**Estimated effort:** 3–5 working days.

**Outcome:** A clean checkout can start every local dependency and host with one documented/manual sequence.

**Files:** `local/compose.yml`, `local/servicebus/Config.json`, `.env.example`, readiness/migration scripts, Function `local.settings.json.example` files, local README section, and `CloudOrders.EndToEndTests`.

**Tasks:**

- [ ] Pin SQL Server, Service Bus emulator, emulator SQL dependency, and Azurite images; use health checks, named volumes, loopback-only ports, and `MSSQL_SA_PASSWORD`.
- [ ] Configure `orders` queue lock, TTL, duplicate detection, max delivery count, and DLQ behavior.
- [ ] Add a PowerShell readiness script that prints URLs and never prints secret values.
- [ ] Add service-level tests for healthy, broker outage/recovery, duplicate, transient processor retry, poison/DLQ, and replay scenarios.
- [ ] Add safe cleanup that does not delete named volumes unless the operator explicitly asks.

**Manual test:** Follow README from a clean checkout: trust HTTPS certificate, start Compose, wait for health, apply migrations, start the API and both Functions, create/read an order through HTTP, observe `Pending → Processing`, and run the local service-level E2E smoke test. The browser journey is added after the Web project exists in Sprint 7.

**Deploy gate:** Local stack is reproducible without undocumented steps, the same commit is redeployed to development, and the Azure order-processing regression smoke remains green. Evidence is stored under `docs/evidence/sprint-4/`. Commit `test: make local order flow reproducible`.

**Natural commit checkpoints:** Compose health/config; readiness and migration scripts; service-level tests; manual evidence and documentation.

## Sprint 5 — Azure Foundation and Identity Graph

**Estimated effort:** 8–12 working days.

**Outcome:** The working development resources from Sprints 0.6–3 are hardened through reviewed Bicep with private data-service connectivity, explicit identities, and least-privilege RBAC.

**Decision gate:** Reuse the approved development tenant, subscription, `ukwest` region, resource groups, and naming scheme. Before deployment, confirm the development/test budget accepts the Premium SKUs required by private endpoints, confirm the Azure-native deployment-job identity owner, and approve the narrow ACR Tasks network exception if an explicit stable task-bypass setting is not available. Prompt before changing those values or selecting production budget and alert owners.

**Files:** `infra/main.bicep`, `infra/foundation.bicep`, `infra/modules/{network,private-dns,monitoring,sql,service-bus,acr,acr-task,key-vault,identities,role-assignments,deployment-job}.bicep`, `infra/environments/development.bicepparam`, SQL bootstrap/migration project, and what-if evidence.

**Tasks:**

- [ ] Verify current stable Bicep/AVM versions, `ukwest` regional support, private-endpoint capabilities, and required SKUs/cost for SQL, Service Bus, Storage, ACR, Key Vault, Log Analytics, and managed identities. As verified on 2026-08-16, dedicated ACR Tasks agent pools are preview-only and unavailable in `ukwest`; do not plan one unless the official support list changes.
- [ ] Add resource group, tags, non-overlapping VNet, Container Apps `/27+`, Flex integration `/26`, private-endpoint `/27+`, private DNS, SQL, Service Bus, ACR, Log Analytics, and Application Insights.
- [ ] Add environment-specific managed identities and least-privilege Azure roles; do not assign runtime `Owner`/`Contributor`/`db_owner`.
- [ ] Add a migration/security bootstrap that creates contained SQL users and roles outside application startup.
- [ ] Add a pinned/scanned, no-ingress, VNet-integrated Container Apps deployment/migration Job that is invoked through Azure Resource Manager by the protected GitHub-hosted workflow's environment-scoped OIDC identity; do not register it as a GitHub Actions runner and do not execute untrusted pull-request code.
- [ ] Give the deployment Job identity only the private data-plane and SQL-bootstrap permissions its command requires. Let the GitHub-hosted workflow retain reviewed Azure control-plane deployment authority; the Job must not receive subscription-wide deployment rights.
- [ ] Define an ACR Task that builds from the exact public-repository commit using a system-assigned identity, scans the resulting image, and records its immutable digest. Prefer an explicit stable task network-bypass setting with public access disabled; otherwise allow only the published regional `AzureContainerRegistry` service-tag IPv4 ranges required by managed Tasks and prove all other public paths fail. For that fallback, resolve the current prefixes and `changeNumber` from the official service-tag feed during every infrastructure deployment, fail closed if resolution fails, review additions, remove obsolete prefixes, and run a scheduled weekly drift check. Never attempt Docker/privileged builds inside Container Apps.
- [ ] Record an ADR for the public-repository/private-deployment design: prohibit self-hosted GitHub runners, explain the Azure-native Job and ACR Task split, document why the `ukwest` service-tag fallback may be required, define its monitoring/removal conditions, and revisit it when a stable fully private task path becomes regionally available.
- [ ] Transfer immutable Function/migration artifacts by having the Job obtain a short-lived, read-only GitHub App installation token from a Key Vault-held bootstrap key, download the exact workflow-run artifacts, verify their recorded SHA-256 values, and write them to private deployment storage. Do not store a personal access token or long-lived artifact URL.
- [ ] Seed the ACR Task/deployment Job images through an explicitly approved time-bounded bootstrap path, remove that path, and prove restricted ACR build/push/pull plus private Function package/migration execution before closing the sprint.

**Implementation references:** [GitHub self-hosted runner security](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/manage-access), [ACR task network-bypass policy](https://learn.microsoft.com/azure/container-registry/manage-network-bypass-policy-for-tasks), [ACR private endpoint build behavior](https://learn.microsoft.com/azure/container-registry/container-registry-private-endpoints), [Azure service-tag discovery and weekly updates](https://learn.microsoft.com/azure/virtual-network/service-tags-overview), and [Container Apps privileged-container restriction](https://learn.microsoft.com/azure/container-apps/containers).
- [ ] Run `az bicep build`, `az deployment sub validate`, and `az deployment sub what-if`; review destructive changes manually.

**Manual test:** From the protected workflow, start the Azure-native deployment Job, retain its output, resolve private DNS, and prove SQL login, Service Bus send/receive, Storage access, restricted ACR build/pull, and artifact checksum verification with the intended identities; prove unauthorized identities and non-approved public ACR paths fail.

**Deploy gate:** Development foundation and workloads are redeployed with clean what-if, private-connectivity checks, least-privilege negative tests, and the Azure order journey green. Commit `infra: harden development Azure foundation`.

**Natural commit checkpoints:** Bicep module groups; environment parameters; identity/RBAC; SQL bootstrap; what-if and private-connectivity evidence.

## Sprint 6 — Azure Functions Hosting

**Estimated effort:** 4–6 working days.

**Outcome:** The minimum development Function hosts introduced in Sprint 3 are hardened into two production-shaped .NET 10 isolated Linux Flex Consumption apps with private connectivity, managed-identity access, telemetry, scaling limits, and rollbackable packages.

**Files:** Function host Bicep modules, `host.json`, `Program.cs`, app settings templates, package manifest scripts, telemetry configuration, and `.github/workflows/deploy-functions.yml`.

**Tasks:**

- [ ] Recheck current Flex/.NET 10/runtime/VNet support for the approved region.
- [ ] Replace any Sprint 3 bootstrap networking/settings with separate private storage accounts, identity-based host storage, Service Bus sender/receiver roles, SQL roles, Application Insights, timer schedule, batch/lock/timeout settings, and no public inbound product endpoint.
- [ ] Produce deterministic zip packages and SHA-256 manifests; deploy an immutable package and record the manifest.
- [ ] Add smoke commands for timer invocation, Service Bus processing, telemetry, and rollback to the previous package.

**Manual test:** Create an order through the local/dev API, verify both Functions process it with managed identity, inspect structured telemetry, and redeploy the previous package to prove rollback.

**Deploy gate:** Functions process a test order in Azure without secrets or SQL passwords. Commit `feat: deploy isolated Functions on Flex Consumption`.

**Natural commit checkpoints:** host/runtime settings; publisher deployment; processor deployment; identity/telemetry; rollback evidence.

## Sprint 7 — Web Delivery and Authentication Foundation

**Estimated effort:** 4–6 working days.

**Outcome:** An authenticated user can load the deployed standalone WASM shell and make an authorized same-origin API request through the linked Static Web Apps `/api` path.

**Decision gate:** Reuse the Sprint 2 API registration and prompt for the Entra frontend public-client registration, exact redirect URIs, non-production test users, and approved production domain. Reuse the existing GitHub environments unless the user changes them.

**Files:** API Dockerfile/`.dockerignore`; Container App Bicep; `src/CloudOrders.Web/{Auth,Services,wwwroot}`; `src/CloudOrders.Api.Client`; Static Web Apps Bicep/workflow and `staticwebapp.config.json`; focused client/auth tests; Playwright authentication setup.

**Interfaces:** standalone WASM public client; Entra authorization-code flow with PKCE; `IOrdersClient`; same-origin `/api/v1`; `CloudOrders.Orders.Read`/`CloudOrders.Orders.Write` scopes with the `OrderUser` app role; no browser client secret.

**Tasks:**

- [ ] Build and scan a patched, non-root, deterministic .NET 10 API image with immutable commit-SHA tagging and an OpenAPI compatibility artifact.
- [ ] Scaffold the standalone .NET 10 WASM host and focused typed client; centralize bearer, W3C trace, durable idempotency-key, timeout/cancellation, Problem Details, and bounded safe-GET retry behavior.
- [ ] Add Entra PKCE authentication, API scope/role policies, return URL handling, and an explicitly guarded local-development identity; production startup/configuration must reject the synthetic handler.
- [ ] Provision Static Web Apps Standard with AVM where supported, link the Container App backend, and configure exact CORS/headers, CSP, SPA fallback, no-cache shell, immutable assets, and generated-origin denial tests.
- [ ] Add focused client/auth tests and a Playwright smoke that signs in, loads the shell, calls an authorized API endpoint, and proves an unauthenticated/unauthorized request is rejected.

**Manual test:** Sign in as the non-production `OrderUser`, load the deployed shell, retrieve the first authorized customer-history page through `/api`, refresh deep-linked navigation, sign out, and verify direct unauthenticated and generated-origin access are denied.

**Deploy gate:** The API image and WASM artifact deploy immutably to development; authentication, authorization-negative, linked-backend, and shell smoke checks pass. Commit `feat: establish authenticated web delivery`.

**Natural commit checkpoints:** API image/OpenAPI evidence; typed client and handler tests; Entra auth boundary; Static Web Apps IaC; deployed browser smoke.

## Sprint 8 — Frontend Shell and Design System

**Estimated effort:** 4–6 working days.

**Outcome:** The deployed site has a distinctive, responsive, accessible dispatch-control shell and reusable UI primitives, without yet pretending unfinished order workflows are complete.

**Files:** `src/CloudOrders.Web/{Layout,Components/Forms,Components/Feedback,Components/Orders,Pages/DesignSystem,wwwroot/css,wwwroot/fonts}`; `tests/CloudOrders.Web.Tests`; `docs/evidence/sprint-8/`.

**Interfaces:** design tokens from handoff section 19; `MainLayout`; `PageTitle`; `ValidationSummary`; `LoadingState`; `EmptyState`; `ErrorState`; `OrderRoute`; accessible navigation and live-status region.

**Tasks:**

- [ ] Use the `frontend-design` skill during execution to translate the approved dispatch-control direction into tokens, local font assets, responsive spacing/type scales, focus treatment, and representative narrow/wide layouts.
- [ ] Implement semantic landmarks, skip navigation, authenticated navigation, page framing, feedback primitives, form fields, confirmation dialog, copy action, and restrained `aria-live` behavior.
- [ ] Add an authenticated development/test-only design-system page that renders every shared component state for manual Azure review; add a production exclusion test and no data-mutation controls.
- [ ] Implement the accessible `Received → Processing` business route with text/icon redundancy and reduced-motion/high-contrast behavior; do not expose infrastructure states to ordinary users.
- [ ] Write bUnit tests for landmarks, focus targets, feedback variants, route states, reduced-motion markup, authorization rendering, and component keyboard behavior.
- [ ] Add automated accessibility and responsive smoke checks for the deployed shell, plus a documented manual keyboard, zoom/reflow, high-contrast, and screen-reader procedure.

**Manual test:** Navigate the development site using only the keyboard at desktop and narrow mobile widths; exercise every shared state, 200–400% zoom/reflow, reduced motion, and one screen-reader smoke without focus loss or color-only meaning.

**Deploy gate:** Release publish, bUnit, automated accessibility, responsive browser smoke, and manual accessibility evidence pass against the Azure development site. Commit `feat: add accessible dispatch control shell`.

**Natural commit checkpoints:** tokens/fonts; responsive shell/navigation; feedback/form primitives; order route; bUnit/accessibility and deployed evidence.

## Sprint 9 — Order Workflows

**Estimated effort:** 5–8 working days.

**Outcome:** An authenticated operator can create, find, refresh, and track orders through complete business workflows against the real development API.

**Files:** `src/CloudOrders.Web/Pages/{Home,CreateOrder,OrderDetails,CustomerOrders}.razor`; focused components and state services; typed-client order operations; `tests/CloudOrders.Web.Tests`; core Playwright page objects/specs.

**Interfaces:** `POST /api/v1/orders`; `GET /api/v1/orders/{orderId}`; `GET /api/v1/customers/{customerReference}/orders`; UUID `Idempotency-Key`; stable cursor/query state; UI labels `Received` (API `pending`) and `Processing` (API `processing`).

**Tasks:**

- [ ] Write failing typed-client and bUnit tests for create validation/focus, durable idempotent submission, exact replay/conflict, cancellation, Problem Details field/page mapping, and stale-response suppression.
- [ ] Implement Home lookup and recent orders for the active/selected authorized customer, Create Order review/submission/success, Order Details polling/refresh, and Customer History pagination with URL-preserved cursor/page-size state; do not imply an all-customer aggregate or expose filter/sort controls unsupported by the API contract.
- [ ] Implement explicit loading, empty, retryable failure, authorization failure, rate-limit, dependency-timeout, conflict, and session-expiry transitions with safe next actions.
- [ ] Render UTC values in local time with visible zone context, preserve customer-scope authorization, prevent duplicate visual submission, and never automatically replay POST without its original idempotency key.
- [ ] Add Playwright journeys for create, refresh/deep link, find/history pagination, `Received → Processing`, validation, replay, conflict, and recoverable API failure. Include a deterministic route fixture that lets the first POST reach the server, withholds its response from the browser, and verifies the UI retry preserves the captured idempotency key.

**Manual test:** In development, sign in, create an order, refresh its deep link, locate it through the active customer's history, and observe `Received → Processing`. Then run the checked-in idempotent-retry Playwright journey against development; it deterministically withholds the first committed response and verifies the UI retry creates only one order with the same key. Conflict mapping remains automated because the ordinary UI does not expose idempotency keys.

**Deploy gate:** The immutable API/WASM release deploys to development and passes bUnit plus the core browser journey against Azure SQL, Service Bus, and Functions. Commit `feat: deliver operator order workflows`.

**Natural commit checkpoints:** client contract tests; create flow; details/status flow; lookup/history flow; error/idempotency handling; Playwright and deployed evidence.

## Sprint 10 — Frontend Quality and Release Integration

**Estimated effort:** 4–6 working days.

**Outcome:** The complete business UI is resilient, accessible, observable, version-compatible, and rollbackable across supported desktop and mobile browser profiles.

**Files:** authentication/error pages; browser telemetry adapter; Playwright configuration/projects; accessibility checks; bundle-budget script; Static Web Apps release/rollback workflow; `docs/evidence/sprint-10/`.

**Interfaces:** access-denied/session-expiry return flow; W3C browser trace context; redacted browser telemetry; versioned OpenAPI/client compatibility; immutable WASM artifact hash in the release manifest.

**Tasks:**

- [ ] Finish sign-in progress, access-denied, expired-session, return-to-original-route, cancellation-on-navigation, bounded safe retry, offline/network failure, and stale-response tests and behavior.
- [ ] Add browser telemetry for navigation, safe user actions, unhandled errors/rejections, and API correlation without tokens or sensitive form values; verify consent/redaction settings.
- [ ] Run automated WCAG checks plus documented keyboard/screen-reader review over representative success, loading, empty, validation, and failure states; fix all release-blocking WCAG 2.2 AA findings.
- [ ] Add Chromium, Firefox, WebKit, and representative mobile Playwright projects; fail on unexpected console errors and retain trace/screenshots/video only under the documented artifact policy.
- [ ] Enforce compressed bundle budgets, cache/source-map policy, stale-browser/API rolling compatibility, same-artifact promotion, Azure smoke, and rollback to the previous WASM artifact.

**Manual test:** Complete the order journey with keyboard and screen reader smoke, exercise session expiry and a recoverable network failure, inspect redacted telemetry, test narrow/mobile layouts, then roll development back and forward between two compatible frontend artifacts.

**Deploy gate:** Cross-browser, accessibility, telemetry, bundle, rolling-compatibility, deployment, and rollback checks pass against development. Commit `test: harden frontend release quality`.

**Natural commit checkpoints:** auth/resilience states; telemetry/redaction; accessibility fixes/evidence; browser matrix; performance/compatibility; deployment rollback evidence.

## Sprint 11 — TestSupport and Observability Evidence

**Estimated effort:** 7–10 working days.

**Outcome:** Non-production reliability scenarios are safely controllable and produce correlated UI, SQL, broker, and Azure Monitor evidence.

**Decision gate:** Confirm the development/test alert action owner, Azure Monitor query identity, evidence retention, and expected telemetry-ingestion cost before enabling alerts or automated log queries.

**Files:** `CloudOrders.TestSupport.Api`, `testsupport.ScenarioLeases` migration, fault policies, Observability Lab components, Playwright fixtures/specs/reporters, `ops/kql`, workbook/alert Bicep, and production exclusion tests.

**Tasks:**

- [ ] Implement bounded leases, allowlisted faults, automatic expiry, cleanup, TestOperator authorization, rate limits, audit events, and DLQ replay.
- [ ] Add the authorized non-production Observability Lab with scenario selection, scope/duration confirmation, lease status, progress timeline, correlation IDs, cleanup, KQL copy actions, and Portal links.
- [ ] Add W3C trace/test-run/scenario/order/event propagation through API, Outbox, Service Bus, Functions, and browser telemetry.
- [ ] Add required lifecycle events and KQL for healthy, retry, duplicate, poison/DLQ, replay, and telemetry-silence scenarios.
- [ ] Add serial Azure-observability Playwright coverage and tests proving TestSupport, fault injection, synthetic identities, and diagnostic routes are absent from production templates/workflows.

**Manual test:** Run one healthy and one faulted development scenario; verify UI state, database state, broker settlement/DLQ, trace IDs, KQL results, cleanup, lease expiry, and alert behavior agree.

**Deploy gate:** TestSupport and Observability Lab deploy only to development/test; every supported scenario has retained evidence and safe cleanup, while the production what-if contains neither workload. Commit `test: add non-production observability lab`.

**Natural commit checkpoints:** TestSupport lease/safety controls; fault policies; Observability Lab; telemetry propagation/KQL; Playwright scenarios; production exclusion tests.

## Sprint 12 — CI/CD, Promotion, and Operations

**Estimated effort:** 7–10 working days.

**Outcome:** Pull requests produce trusted evidence; protected promotion deploys the same immutable artifacts through environments.

**Decision gate:** Reuse the public `RobertMagowan/ordersApp` repository and current protected environments. Prompt for test/production deployment identities, alert/action-group owners, budgets, and any new environment reviewer requirement.

**Files:** `.github/workflows/{ci,infrastructure,deploy-apps,e2e,scheduled-e2e,load}.yml`, release manifest generator, `ops/runbooks/*`, budgets/alerts Bicep, Dependabot/security configuration, and README deployment docs.

**Tasks:**

- [ ] CI: restore, format, build, unit/integration/contract/bUnit, OpenAPI diff, WASM publish, container/IaC/dependency/secret scans, local Playwright subset, and reviewed immutable GitHub Action pins where practical.
- [ ] Infrastructure workflow: OIDC login, Bicep build/validate/what-if, protected deployment, branch-safe manual dispatch, concurrency, evidence retention, and deployed endpoint/change summary in `$GITHUB_STEP_SUMMARY`.
- [ ] Application workflow: trigger the private ACR Tasks build, run the Azure-native deployment/migration Job, deploy the WASM artifact, smoke-test, run protected E2E, and publish one release manifest containing every immutable digest/hash. Promote the API by server-side ACR import/trusted-service path or another reviewed private-registry mechanism and never rebuild between environments.
- [ ] Add rollback/runbooks for DLQ replay, stuck outbox, failed migration, identity/network failure, telemetry silence, restore, and release rollback.
- [ ] Add NBomber test-environment workloads with cost guardrails, dependency/queue protection, and published results/metrics.

**Manual test:** Open a PR and verify unprivileged checks; merge to development and verify immutable artifact deployment, smoke, E2E, telemetry, summary/evidence, and rollback; promote the same manifest through a development → test PR.

**Deploy gate:** `ordersApp` can build and promote the same trusted artifacts through development and test without long-lived Azure secrets. Commit `ci: add protected artifact promotion`.

**Natural commit checkpoints:** CI/security checks; infrastructure OIDC workflow; application artifact workflow; release manifest/promotion; runbooks/alerts; test-environment load evidence.

## Sprint 13 — Production Readiness

**Estimated effort:** 5–8 working days.

**Outcome:** The system is supportable, secure, cost-controlled, and ready for a reviewed production promotion.

**Tasks:**

- [ ] Run the complete Release, SQL migration-upgrade, contract, unit, integration, bUnit, Playwright, accessibility, security, load, restore, and rollback matrix from a clean checkout.
- [ ] Verify private networking, TLS, API edge/linking, CORS, rate limits, request limits, identity negative tests, backup/retention, budgets, quotas, tags, and region support.
- [ ] Remove bootstrap firewall rules, credentials, unused resources, development fault hooks, TestSupport production paths, and obsolete documentation.
- [ ] Have an independent operator execute the runbooks and record evidence under `docs/evidence/sprint-13/`; if unavailable, record the solo rehearsal and explicitly accept the residual operational risk before production.
- [ ] Prompt for final production approval and domain/owner confirmation; do not create or update production resources without it.

**Manual test:** After explicit production approval, promote the exact test release through a test → master PR; verify the custom HTTPS domain, sign-in, create/find/status journey, correlated telemetry, alerts, backup state, and rollback reference without using development identities or TestSupport.

**Deploy gate:** Handoff section 35 is fully evidenced, the exact test artifact is promoted to production through a test → master PR, post-deployment smoke is green, and production approval is recorded. Commit `release: complete CloudOrders version one readiness`.

**Natural commit checkpoints:** final test matrix; security/network review; restore/rollback evidence; runbook rehearsal; production approval record.

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
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
```

PowerShell execution policy may block `npm.ps1`; use `npm.cmd` for Playwright commands when necessary. Never treat a green unit test run as a substitute for the sprint's manual flow or deploy gate.

## Plan Self-Review

- **Spec coverage:** repository/promotion foundations (S0–0.6), application contracts and durability (S1–2), outbox/inbox and Azure messaging (S3), local reproducibility (S4), Azure network/RBAC hardening (S5), Flex hosting (S6), web edge/authentication (S7), frontend foundation (S8), order workflows (S9), frontend quality (S10), TestSupport/observability (S11), CI/CD/operations (S12), and production readiness (S13) are each assigned.
- **Frontend scope correction:** the former single web sprint is split into four independently reviewable increments. S7 proves hosting/auth/API connectivity, S8 proves the accessible design system, S9 proves business workflows, and S10 proves cross-browser quality and rollback. The Observability Lab remains in S11 with its safety API.
- **Azure deployability correction:** every delivered feature sprint now ends with an Azure development deployment or regression deployment. S2 introduces the minimum Azure SQL dependency, S3 introduces minimum Service Bus/Function hosting, and S5 hardens those already-working resources rather than postponing all cloud testing.
- **Sequence check:** each frontend sprint consumes stable API/auth contracts from earlier sprints; TestSupport follows the business UI; release automation follows all deployable artifacts. No later sprint is required to make an earlier sprint's stated manual journey possible.
- **Placeholder scan:** no unowned placeholder implementation steps remain. Tenant/app IDs, test users, production domain, budgets, alert owners, and production approval are explicit user decision gates because they cannot be safely inferred.
- **Type/interface consistency:** `OrderCreatedIntegrationEventV1`, `OutboxDispatcher.DrainAsync(CancellationToken)`, `OrderProcessor.ProcessAsync(ServiceBusReceivedMessage, CancellationToken)`, `IIdempotencyStore`, and `/api/v1` retain their semantics. The UI explicitly maps API `pending` to the user-facing `Received` label.
- **Security and operations:** browser secrets, automatic non-idempotent POST retry, self-hosted GitHub runners for this public repository, development identities in production, unrestricted public data services after Sprint 5, mutable artifacts, and production TestSupport are prohibited and have negative-test gates. Any temporary or managed-service network exception is explicit, least scoped, monitored, and regression tested.
- **Effort review:** 76–115 working days is a planning range for one developer, not elapsed calendar time. Re-estimate at each sprint boundary using measured throughput and newly discovered Azure constraints.
