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
- For each remaining sprint, the developer first completes implementation and focused automated tests; a separate high-capability verification agent then spends three working days testing the deployed `development` release; a separate QA-only agent spends one to two working days testing the promoted Azure `test` release. The test approach is chosen for the technology and risk: domain/API uses unit, integration, contract, concurrency, authorization-negative, and deployed HTTP tests; data/messaging uses Testcontainers/emulators, recovery drills, and Azure service flows; IaC/CI uses lint, build, parameter validation, what-if, RBAC negatives, workflow evidence, and rollback; frontend uses bUnit, accessibility, Playwright, responsive/cross-browser, and user journeys.
- Retain evidence under `docs/evidence/sprint-<number>/`: run/deployment URLs, immutable artifact or release ID, commands/results, development-verification outcome, QA outcome, defects, and re-test record. QA agents do not edit implementation. Each defect is fixed on a fresh `feature/*` branch, reviewed, and promoted through the same development and test gates; unresolved defects block `test` to `master` promotion.
- Prompt the user before using values not discoverable locally: GitHub owner/visibility, Azure tenant/subscription/region, production domain, Entra app registrations, alert recipients, or budget owners.

## Delivery Status and Effort Model

Estimates assume one focused developer. Remaining-sprint estimates explicitly include implementation, focused automated tests, Azure development deployment, three independent development-verification days, and one to two QA days in Azure `test`. Defect remediation, waiting for external access/approvals/DNS/quota, and Azure incidents are excluded and re-estimated when discovered.

| Sprint | Delivery | Status | Estimated effort |
|---|---|---|---:|
| 0 | Repository bootstrap | Baseline delivered | 1–2 days |
| 0.5 | DevOps promotion foundation | Baseline delivered | 2–3 days |
| 0.6 | MVP Azure container deployment and AVM hardening | Baseline delivered | 3–5 days |
| 1 | Domain, contracts, and API vertical slice | Baseline delivered to development | 4–6 days |
| 2 | Workflow, contract, and test-environment assurance | Complete in development and test | 6–8 days |
| 3 | SQL schema and durable HTTP idempotency | Complete in development and test; no production deployment | 9–12 days |
| 4A | External ID identity and ownership vertical slice | Planned | 16–22 days |
| 4B | Customer history and ownership-contract completion | Planned; depends on 4A | 16–21 days (reset) or 19–25 days (mapped backfill) |
| 5 | Outbox, inbox, Service Bus, and development Functions | Planned | 11–15 days |
| 6 | Reproducible local platform | Planned | 7–10 days |
| 7 | Azure security and network foundation | Planned | 12–17 days |
| 8 | Production-grade Azure Functions hosting | Planned | 8–11 days |
| 9 | Web delivery and authentication foundation | Planned | 7–10 days |
| 10 | Frontend shell and design system | Planned | 8–11 days |
| 11 | Order workflows | Planned | 9–13 days |
| 12 | Frontend quality and release integration | Planned | 8–11 days |
| 13 | TestSupport and observability evidence | Planned | 11–15 days |
| 14 | CI/CD, promotion, and operations | Planned | 11–15 days |
| 15 | Production readiness | Planned | 9–13 days |

The revised roadmap contains 19 sprint phases: three foundation phases, Sprints 1–3, Sprint 4A, Sprint 4B, and Sprints 5–15. The total estimated effort is **158–220 working days** for the reset path or **161–224 working days** for the mapped-backfill path. With Sprints 0–3 delivered in non-production, approximately **133–184** (reset) or **136–188** (mapped-backfill) working days remain before defect remediation. Estimates exclude external access/approval delays, Azure incidents, and the elapsed 14-calendar-day D2 soak.

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

## Sprint 2 — Workflow, Contract, and Test-Environment Assurance

**Estimated effort:** 2–3 implementation days + 3 development-verification days + 1–2 QA days = 6–8 working days.

**Outcome:** The protected workflow is secure and reviewable, the frontend/API contract pack is repository-owned, and the first `test` Azure environment can receive the same immutable release promoted from `development`.

**Decision gate:** Reuse the approved tenant, subscription, `ukwest` region, and environment resource-group pattern. Confirm the `test` resource-group naming, budget owner, and test deployment identity before provisioning billable Azure resources.

**Tasks:**

- [ ] Audit delivered Sprints 0–1 against Git, CI, and Azure evidence; carry any gap as a named correction.
- [ ] Upgrade every workflow action to a Node 24-compatible release, pin it to a verified full-length commit SHA with a readable version comment, reject manual dispatch from invalid promotion branches, remove `curl --insecure`, write endpoint/release/immutable artifact details to `$GITHUB_STEP_SUMMARY`, and keep the public bootstrap image as a one-time creation path.
- [ ] Add `docs/contracts/` containing versioned copies of handoff section 19 and sections 25–35, plus a traceability map from each requirement to its project, tests, and sprint gate.
- [ ] Provision the smallest protected Azure `test` resource group/environment needed to deploy the existing immutable API artifact; restrict GitHub environments and OIDC identities to the promotion branch, and validate Bicep parameters and what-if before deployment.
- [ ] Add deterministic workflow evidence and rollback instructions proving a `development` release can be promoted through a reviewed `development` → `test` PR without rebuilding.

**Development verification (3 days):** A separate verifier performs CI/action-pinning inspection, branch-policy negative tests, Bicep build/parameter/what-if checks, deployment and rollback rehearsals, contract-traceability review, and deployed `development` smoke tests; retain results under `docs/evidence/sprint-2/`.

**QA in test (1–2 days):** A QA-only agent validates the approved `development` → `test` promotion, deployed `/health/live` smoke, immutable-release identity, evidence retention, and a defect-return rehearsal in Azure `test`.

**Deploy gate:** The reviewed same artifact deploys to both non-production environments without insecure TLS bypass or long-lived Azure secrets. Commit focused workflow, contract-pack, test-environment, and evidence changes separately.

## Sprint 3 — SQL Schema and Durable HTTP Idempotency

**Estimated effort:** 5–7 implementation days + 3 development-verification days + 1–2 QA days = 9–12 working days.

**Status (2026-08-27):** Complete in `development` and `test`; production was deliberately not deployed. See `docs/evidence/sprint-3/`.

**Outcome:** Repeated or concurrent POSTs cannot create duplicate orders, and the API is deployable against SQL Server before Entra authorization is enabled.

**Files:** EF configurations and `Orders`, `OutboxMessages`, `IdempotencyRecords` models; migrations; `Idempotency-Key` middleware/handler; SQL integration tests using Testcontainers; initial SQL service in `local/compose.yml`; focused Azure SQL Bicep modules; deployment migration step.

**Interfaces:** `IIdempotencyStore.TryGetAsync(subjectId, key)`; `IdempotencyRecord`; `POST` requires a UUID `Idempotency-Key`; responses are 201 first-use, 200 exact replay with `Idempotency-Replayed: true`, 409 payload conflict.

**Tasks:**

- [x] Write failing SQL tests for first request, exact replay, same-key/different-payload conflict, and two concurrent requests.
- [x] Add schema/indexes and one atomic transaction covering Order + IdempotencyRecord + a complete publishable OutboxMessage: stable `EventId`/`Id`, `AggregateId`, message metadata/payload, occurrence timestamps, W3C trace context, and initial pending state.
- [x] Implement a durable key plus canonical request hash using a temporary non-production subject abstraction; move Entra `oid` binding and customer authorization to Sprint 4.
- [x] Add migrations that target an explicit connection string, a pinned local SQL health check, request limits/timeout/unknown-member validation tests, and an Azure SQL deployment migration step that never runs from application startup.
- [x] Provision development Azure SQL and minimum API database access; any temporary bootstrap firewall exception has a named owner/expiry and moves to the Sprint 7 removal register.

**Development verification (3 days):** A separate verifier runs Testcontainers concurrency/restart/replay tests, migration upgrade/rollback rehearsal, HTTP negative tests, and deployed Azure SQL first-use/replay/conflict smoke tests.

**QA in test (1–2 days):** A QA-only agent promotes the immutable release to Azure `test`, validates migration evidence and first-use/exact-replay/conflict behavior, and records all outcomes under `docs/evidence/sprint-3/`.

**Deploy gate:** The protected workflow validates Bicep, applies the reviewed migration, deploys the immutable API image, and proves exactly one Order, Outbox row, and IdempotencyRecord for concurrent idempotent submissions.

## Sprint 4A — External ID Identity and Ownership Vertical Slice

**Estimated effort:** 12–17 implementation/release days (including the separate E1 migration-only release) + 3 development-verification days + 1–2 QA days = **16–22 working days**.

**Outcome:** A verified Microsoft Entra External ID customer can authenticate, discover their opaque server-generated customer reference through `GET /api/v1/me`, create/read/replay only their own orders, and an explicitly assigned `user.admin` can act across customer records. Default customers receive no app-role assignment; `user.admin` is the only elevated product role and never grants directory administration.

**Decision gate:** Before any D1 authenticated traffic, the named data owner records a separate development/test `reset` or `mapped-backfill` decision. A reset is permitted only for synthetic/disposable data. A mapping is protected outside Git and must be one-to-one from legacy `CustomerReference` to exact external-tenant `(issuer, oid)`; email is never an identity key. The external tenant, API/public-client registrations, email OTP flow, workforce federation, one federated work-account `user.admin`, protected environment configuration, and recovery administrators must exist before the first **D1** development merge; E1 uses a separate migration-only release that preserves the active API image and traffic.

**Tasks:**

- [ ] Reconcile contracts and all later-sprint references to use bare delegated scopes `Orders.Read`/`Orders.Write`, exact role `user.admin`, verified External ID customer, `CustomerProfileId`, and safe absent/foreign `404`; correct migration-job polling to capture and poll its exact started execution.
- [ ] Add real JWT bearer validation for exact signature/lifetime/issuer/tenant/audience/client/`oid`/delegated-scope checks. Use fake authentication only for policy tests; signed local JWTs own cryptographic and malformed-claim negatives.
- [ ] Add E1 `CustomerProfiles` keyed by exact issuer plus `oid`; server-generated immutable customer references; nullable profile ownership columns; deterministic D1 legacy idempotency subject `profile:{ActorCustomerProfileId:N}`; race-safe profile creation; and additive migration compatibility proof.
- [ ] Before D1 traffic, promote the protected E1 migration-only release through both non-production environments while it preserves the active API image/traffic. Then, separately for each environment immediately before D1, quiesce order traffic, perform the approved data transition transaction, and recheck zero unowned orders, zero legacy idempotency rows, and no duplicate future actor/key values. Sprint 3 is E1 schema-compatible but is never an authorization rollback target after D1.
- [ ] Add ASP.NET Core scope/resource policies, owner-bearing reads, actor/target idempotency, allowlisted authorization audit, `GET /api/v1/me`, and v1 POST/GET ownership behavior. `/me` requires `Orders.Read`; create requires `Orders.Write`; cross-customer resource denial is a safe `404`.
- [ ] Promote R1 (E1 + D1) to development and test. After D1, rollback is only to a recorded authenticated D1 revision or a fail-closed order-ingress outage—never to Sprint 3.

**Development verification (3 days):** A separate verifier performs local schema/migration/SQL-state inspection; real bearer and policy matrices; authenticated own/foreign/admin/revoke/replay/concurrency tests; audit-redaction checks; workflow/Bicep/configuration checks; and Azure development smoke using two OTP customers plus the federated administrator.

**QA in test (1–2 days):** A QA-only agent tests the promoted R1 release to destruction: authentication and scope failures, two-customer non-disclosure, administrator access/revocation with fresh tokens, POST/GET/replay/concurrency/recovery, direct SQL profile/order/idempotency integrity, exact revision/digest/job execution, TLS, and health. History/cursor behavior is not in this sprint.

**Deploy gate:** Both non-production environments run D1 on E1 after independently evidenced transition transactions. The test release passes ownership and rollback/fail-closed checks without exposing an unauthenticated Sprint 3 route.

## Sprint 4B — Customer History and Ownership-Contract Completion

**Estimated effort:** 12–16 implementation/release days + 3 development-verification days + 1–2 QA days = **16–21 working days** for reset data, or 15–20 implementation/release days + the same 3 + 1–2 assurance days = **19–25 working days** for mapped-backfill reconciliation.

**Outcome:** A customer can page only their own history with a target-bound, signed, expiring, rotatable cursor. The idempotency schema completes its expand/migrate/contract sequence without losing a compatible authenticated rollback revision.

**Tasks:**

- [ ] R2: after a new write-quiescence/precondition transaction, add E2 non-null ownership and actor/key idempotency uniqueness while retaining nullable legacy `SubjectId`; deploy unchanged D1 behavior and prove D1 rollback in both environments.
- [ ] H1: add `GET /api/v1/customers/{customerReference}/orders` only after R2 passes in test. Use exact `Orders.Read`, ownership-resource authorization, `(CreatedAtUtc, Id)` newest-first order, page size 1–100, opaque 15-minute cursor, no gaps/duplicates, and safe absent/foreign parity.
- [ ] Add cursor key configuration/runbook. Current/previous material enters only as secure non-production Container App secrets. Validate known key/version/profile/expiry/HMAC; Phase A distributes K2 validation-only, Phase B signs K2 and validates K1, and Phase C removes K1 only after cursor TTL plus the D2 rollback window.
- [ ] R3: deploy D2 that no longer reads/writes/maps `SubjectId` while E2 retains it. Retain exact D1/D2 images and soak D2 for **14 calendar days after the test smoke**; prove D2-to-D1 rollback before contract deletion.
- [ ] R4: after the soak, drop `SubjectId`/obsolete key with E3 and deploy D2-compatible code. Post-E3 rollback is only to retained D2; D1 and Sprint 3 are not valid rollback targets.

**Release gates:** R2, H1, R3, and R4 each receive focused local tests, independent review, Azure development smoke, test targeted QA, direct SQL/migration inspection, immutable digest/revision evidence, and an explicit current rollback target before the next phase. Post-deployment defects use a new remediation `feature/*` branch and block the next phase.

**Final development verification (3 days) and QA in test (1–2 days):** After R4, repeat full local/development/test assurance: signed-JWT negatives locally; live customers/admin scopes/revocation in Azure; history boundaries; cursor target/tamper/expiry/rotation; concurrency/recovery; direct SQL integrity; rollback evidence; and secret/audit redaction. No production deployment occurs.

**Deploy gate:** E3 is allowed only after the recorded D2 soak. Both non-production releases pass the history/cursor and schema-contract matrix, with no unowned data, no legacy `SubjectId`, and a retained authenticated D2 rollback artifact.

## Sprint 5 — Outbox Publisher and Inbox Processor

**Estimated effort:** 7–10 implementation days + 3 development-verification days + 1–2 QA days = 11–15 working days.

**Outcome:** An order moves from `Pending` to `Processing` locally and in Azure development through Service Bus with duplicate-safe, failure-aware processing.

**Files:** `OutboxMessage`/`InboxMessage` persistence; `OutboxDispatcher`; publisher timer Function; processor Service Bus Function; Service Bus emulator/Azurite additions to `local/compose.yml`; emulator config; messaging/unit/integration tests; development Service Bus, storage, identities, minimum Function host Bicep; Function package workflow.

**Interfaces:** `OutboxDispatcher.DrainAsync(CancellationToken)`; `OrderProcessor.ProcessAsync(ServiceBusReceivedMessage, CancellationToken)`; `OrderCreatedIntegrationEventV1`; broker properties `EventId`, `messageType`, `messageVersion`, `traceparent`.

**Tasks:**

- [ ] Write failing tests for bounded outbox chunks, broker-size batching, send-then-mark ordering, broker failure, and crash-after-send.
- [ ] Implement publisher with `ProcessedAt IS NULL`, stored JSON reuse, bounded chunks, and explicit pending-row preservation.
- [ ] Write failing tests for insert-first Inbox claim, duplicate delivery, invalid version/payload, SQL rollback, and settlement failure.
- [ ] Implement one DI scope/DbContext/transaction per processor message with explicit Complete/Abandon/DeadLetter decisions.
- [ ] Add structured fields for `MessageId`, `EventId`, `OrderId`, and `DeliveryCount` without payload secrets.
- [ ] Extend the Sprint 3 Compose file with pinned Service Bus emulator, emulator SQL dependency, and Azurite services sufficient to execute the local publisher/processor flow; Sprint 6 adds full readiness, safe cleanup, and documentation.
- [ ] Provision the development Service Bus queue and minimum isolated Function hosts with managed identities; deploy immutable packages and run the Azure happy path, duplicate, outage/recovery, and poison-message smoke set. Record any temporary public data-service access with owner/expiry for removal in Sprint 7.

**Manual test:** Run the local emulator flow, then create an order through the development API and observe Azure SQL outbox pending → published, Service Bus delivery, Inbox insertion, and `Pending → Processing`. Exercise one duplicate and one controlled poison message and verify settlement/DLQ behavior.

**Deploy gate:** Package both isolated Functions deterministically, deploy them and the API to development, and retain Azure evidence for the happy path and bounded failure drills. Commit `feat: add transactional outbox and inbox processing`.

**Natural commit checkpoints:** failing publisher tests; green publisher; failing processor tests; green processor and settlement; local failure-drill evidence.

## Sprint 6 — Reproducible Local Platform

**Estimated effort:** 3–5 implementation days + 3 development-verification days + 1–2 QA days = 7–10 working days.

**Outcome:** A clean checkout can start every local dependency and host with one documented/manual sequence.

**Files:** `local/compose.yml`, `local/servicebus/Config.json`, `.env.example`, readiness/migration scripts, Function `local.settings.json.example` files, local README section, and `CloudOrders.EndToEndTests`.

**Tasks:**

- [ ] Pin SQL Server, Service Bus emulator, emulator SQL dependency, and Azurite images; use health checks, named volumes, loopback-only ports, and `MSSQL_SA_PASSWORD`.
- [ ] Configure `orders` queue lock, TTL, duplicate detection, max delivery count, and DLQ behavior.
- [ ] Add a PowerShell readiness script that prints URLs and never prints secret values.
- [ ] Add service-level tests for healthy, broker outage/recovery, duplicate, transient processor retry, poison/DLQ, and replay scenarios.
- [ ] Add safe cleanup that does not delete named volumes unless the operator explicitly asks.

**Manual test:** Follow README from a clean checkout: trust HTTPS certificate, start Compose, wait for health, apply migrations, start the API and both Functions, create/read an order through HTTP, observe `Pending → Processing`, and run the local service-level E2E smoke test. The browser journey is added after the Web project exists in Sprint 9.

**Deploy gate:** Local stack is reproducible without undocumented steps, the same commit is redeployed to development, and the Azure order-processing regression smoke remains green. Evidence is stored under `docs/evidence/sprint-6/`. Commit `test: make local order flow reproducible`.

**Natural commit checkpoints:** Compose health/config; readiness and migration scripts; service-level tests; manual evidence and documentation.

## Sprint 7 — Azure Foundation and Identity Graph

**Estimated effort:** 8–12 implementation days + 3 development-verification days + 1–2 QA days = 12–17 working days.

**Outcome:** The working development resources from Sprints 0.6–3 are hardened through reviewed Bicep with private data-service connectivity, explicit identities, and least-privilege RBAC.

**Decision gate:** Reuse the approved development tenant, subscription, `ukwest` region, resource groups, and naming scheme. Before deployment, confirm the development/test budget accepts the Premium SKUs required by private endpoints, confirm the Azure-native deployment-job identity owner, and confirm continued acceptance of the narrow ACR Tasks trusted-service bypass described below. Prompt before changing those values or selecting production budget and alert owners.

**Files:** `infra/main.bicep`, `infra/foundation.bicep`, `infra/modules/{network,private-dns,monitoring,sql,service-bus,acr,acr-task,key-vault,identities,role-assignments,deployment-job}.bicep`, `infra/environments/development.bicepparam`, SQL bootstrap/migration project, and what-if evidence.

**Tasks:**

- [ ] Verify current stable Bicep/AVM versions, `ukwest` regional support, private-endpoint capabilities, and required SKUs/cost for SQL, Service Bus, Storage, ACR, Key Vault, Log Analytics, and managed identities. As verified on 2026-08-16, dedicated ACR Tasks agent pools are preview-only and unavailable in `ukwest`; do not plan one unless the official support list changes.
- [ ] Add resource group, tags, non-overlapping VNet, Container Apps `/27+`, Flex integration `/26`, private-endpoint `/27+`, private DNS, SQL, Service Bus, ACR, Log Analytics, and Application Insights.
- [ ] Add environment-specific managed identities and least-privilege Azure roles; do not assign runtime `Owner`/`Contributor`/`db_owner`.
- [ ] Add a migration/security bootstrap that creates contained SQL users and roles outside application startup.
- [ ] Add a pinned/scanned, no-ingress, VNet-integrated Container Apps deployment/migration Job that is invoked through Azure Resource Manager by the protected GitHub-hosted workflow's environment-scoped OIDC identity; do not register it as a GitHub Actions runner and do not execute untrusted pull-request code.
- [ ] Give the deployment Job identity only the private data-plane and SQL-bootstrap permissions its command requires. Let the GitHub-hosted workflow retain reviewed Azure control-plane deployment authority; the Job must not receive subscription-wide deployment rights.
- [ ] Define an ACR Task that builds from the exact public-repository commit using a system-assigned identity, scans the resulting image, and records its immutable digest. Configure the registry through stable `Microsoft.ContainerRegistry/registries@2025-11-01` (or a newer stable version reverified at implementation) with `publicNetworkAccess: Disabled`, `networkRuleSet.defaultAction: Deny`, `networkRuleBypassOptions: AzureServices`, and `networkRuleBypassAllowedForTasks: true`; use a documented native-Bicep exception if the pinned AVM version does not expose the task-bypass property. Treat `AzureServices` as one registry firewall-bypass contract and inventory its intended consumers: the system-assigned ACR Task, Defender scanning when enabled, and server-side ACR import during promotion. Do not deploy or authorize Container Instances or Machine Learning against the registry. Prove the task succeeds, disabling task bypass produces the expected `403`, and unauthorized identities and direct public clients remain denied. Monitor task invocations and prevent managed-identity tokens from entering logs. Never attempt Docker/privileged builds inside Container Apps.
- [ ] Record an ADR for the public-repository/private-deployment design: prohibit self-hosted GitHub runners, explain the Azure-native Job and managed ACR Task split, define `AzureServices` as the registry's single trusted-services firewall-bypass contract, enumerate the intended ACR Task, optional Defender, and server-side import consumers, document authorization/monitoring/removal conditions, and revisit it when a stable private task agent pool becomes available in `ukwest`.
- [ ] When a `ukwest` fallback needs regional `AzureContainerRegistry` service-tag prefixes in an ACR firewall rule, resolve them from the official service-tag feed during every infrastructure deployment, run a scheduled drift check, review additions/removals, remove obsolete prefixes, and fail closed if current prefixes cannot be resolved. Record that temporary private-endpoint deviation in the ADR.
- [ ] Transfer immutable Function/migration artifacts by having the Job obtain a short-lived, read-only GitHub App installation token from a Key Vault-held bootstrap key, download the exact workflow-run artifacts, verify their recorded SHA-256 values, and write them to private deployment storage. Do not store a personal access token or long-lived artifact URL.
- [ ] Seed the ACR Task/deployment Job images through an explicitly approved time-bounded bootstrap path, remove that path, and prove restricted ACR build/push/pull plus private Function package/migration execution before closing the sprint.

**Implementation references:** [GitHub self-hosted runner security](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/manage-access), [ACR task network-bypass policy](https://learn.microsoft.com/azure/container-registry/manage-network-bypass-policy-for-tasks), [ACR private endpoint build behavior](https://learn.microsoft.com/azure/container-registry/container-registry-private-endpoints), [stable ACR registry API](https://learn.microsoft.com/rest/api/container-registry/registries/create?view=rest-container-registry-2025-11-01), and [Container Apps privileged-container restriction](https://learn.microsoft.com/azure/container-apps/containers).
- [ ] Run `az bicep build`, `az deployment sub validate`, and `az deployment sub what-if`; review destructive changes manually.

**Manual test:** From the protected workflow, start the Azure-native deployment Job, retain its output, resolve private DNS, and prove SQL login, Service Bus send/receive, Storage access, private runtime ACR pull, trusted-service-bypass ACR Task build, and artifact checksum verification with the intended identities. In development, prove disabling task bypass causes the expected `403`, restore it, and prove unauthorized identities and direct public ACR access remain denied.

**Deploy gate:** Development foundation and workloads are redeployed with clean what-if, private-connectivity checks, least-privilege negative tests, and the Azure order journey green. Commit `infra: harden development Azure foundation`.

**Natural commit checkpoints:** Bicep module groups; environment parameters; identity/RBAC; SQL bootstrap; what-if and private-connectivity evidence.

## Sprint 8 — Azure Functions Hosting

**Estimated effort:** 4–6 implementation days + 3 development-verification days + 1–2 QA days = 8–11 working days.

**Outcome:** The minimum development Function hosts introduced in Sprint 5 are hardened into two production-shaped .NET 10 isolated Linux Flex Consumption apps with private connectivity, managed-identity access, telemetry, scaling limits, and rollbackable packages.

**Files:** Function host Bicep modules, `host.json`, `Program.cs`, app settings templates, package manifest scripts, telemetry configuration, and `.github/workflows/deploy-functions.yml`.

**Tasks:**

- [ ] Recheck current Flex/.NET 10/runtime/VNet support for the approved region.
- [ ] Replace any Sprint 5 bootstrap networking/settings with separate private storage accounts, identity-based host storage, Service Bus sender/receiver roles, SQL roles, Application Insights, timer schedule, batch/lock/timeout settings, and no public inbound product endpoint.
- [ ] Produce deterministic zip packages and SHA-256 manifests; deploy an immutable package and record the manifest.
- [ ] Add smoke commands for timer invocation, Service Bus processing, telemetry, and rollback to the previous package.

**Manual test:** Create an order through the local/dev API, verify both Functions process it with managed identity, inspect structured telemetry, and redeploy the previous package to prove rollback.

**Deploy gate:** Functions process a test order in Azure without secrets or SQL passwords. Commit `feat: deploy isolated Functions on Flex Consumption`.

**Natural commit checkpoints:** host/runtime settings; publisher deployment; processor deployment; identity/telemetry; rollback evidence.

## Sprint 9 — Web Delivery and Authentication Foundation

**Estimated effort:** 3–5 implementation days + 3 development-verification days + 1–2 QA days = 7–10 working days.

**Outcome:** A verified External ID customer can load the deployed standalone WASM shell and make an authorized same-origin API request through the linked Static Web Apps `/api` path.

**Decision gate:** Reuse the Sprint 4A External ID API registration and prompt for the Entra frontend public-client registration, exact redirect URIs, non-production test users, and approved production domain. Reuse the existing GitHub environments unless the user changes them.

**Files:** API Dockerfile/`.dockerignore`; Container App Bicep; `src/CloudOrders.Web/{Auth,Services,wwwroot}`; `src/CloudOrders.Api.Client`; Static Web Apps Bicep/workflow and `staticwebapp.config.json`; focused client/auth tests; Playwright authentication setup.

**Interfaces:** standalone WASM public client; External ID authorization-code flow with PKCE; `IOrdersClient`; same-origin `/api/v1`; fully qualified scope requests `api://{api-client-id}/Orders.Read` and `api://{api-client-id}/Orders.Write`, compared in API tokens as bare `Orders.Read`/`Orders.Write`; verified customer default capability with optional exact `user.admin`; no browser client secret.

**Tasks:**

- [ ] Build and scan a patched, non-root, deterministic .NET 10 API image with immutable commit-SHA tagging and an OpenAPI compatibility artifact.
- [ ] Scaffold the standalone .NET 10 WASM host and focused typed client; centralize bearer, W3C trace, durable idempotency-key, timeout/cancellation, Problem Details, and bounded safe-GET retry behavior.
- [ ] Add Entra PKCE authentication, API scope/role policies, return URL handling, and an explicitly guarded local-development identity; production startup/configuration must reject the synthetic handler.
- [ ] Provision Static Web Apps Standard with AVM where supported, link the Container App backend, and configure exact CORS/headers, CSP, SPA fallback, no-cache shell, immutable assets, and generated-origin denial tests.
- [ ] Add focused client/auth tests and a Playwright smoke that signs in, loads the shell, calls an authorized API endpoint, and proves an unauthenticated/unauthorized request is rejected.

**Manual test:** Sign in as a verified non-production External ID customer, load the deployed shell, retrieve the first authorized customer-history page through `/api`, refresh deep-linked navigation, sign out, and verify direct unauthenticated and generated-origin access are denied.

**Deploy gate:** The API image and WASM artifact deploy immutably to development; authentication, authorization-negative, linked-backend, and shell smoke checks pass. Commit `feat: establish authenticated web delivery`.

**Natural commit checkpoints:** API image/OpenAPI evidence; typed client and handler tests; Entra auth boundary; Static Web Apps IaC; deployed browser smoke.

## Sprint 10 — Frontend Shell and Design System

**Estimated effort:** 4–6 implementation days + 3 development-verification days + 1–2 QA days = 8–11 working days.

**Outcome:** The deployed site has a distinctive, responsive, accessible dispatch-control shell and reusable UI primitives, without yet pretending unfinished order workflows are complete.

**Files:** `src/CloudOrders.Web/{Layout,Components/Forms,Components/Feedback,Components/Orders,Pages/DesignSystem,wwwroot/css,wwwroot/fonts}`; `tests/CloudOrders.Web.Tests`; `docs/evidence/sprint-10/`.

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

## Sprint 11 — Order Workflows

**Estimated effort:** 5–8 implementation days + 3 development-verification days + 1–2 QA days = 9–13 working days.

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

## Sprint 12 — Frontend Quality and Release Integration

**Estimated effort:** 4–6 implementation days + 3 development-verification days + 1–2 QA days = 8–11 working days.

**Outcome:** The complete business UI is resilient, accessible, observable, version-compatible, and rollbackable across supported desktop and mobile browser profiles.

**Files:** authentication/error pages; browser telemetry adapter; Playwright configuration/projects; accessibility checks; bundle-budget script; Static Web Apps release/rollback workflow; `docs/evidence/sprint-12/`.

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

## Sprint 13 — TestSupport and Observability Evidence

**Estimated effort:** 7–10 implementation days + 3 development-verification days + 1–2 QA days = 11–15 working days.

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

## Sprint 14 — CI/CD, Promotion, and Operations

**Estimated effort:** 7–10 implementation days + 3 development-verification days + 1–2 QA days = 11–15 working days.

**Outcome:** Pull requests produce trusted evidence; protected promotion deploys the same immutable artifacts through environments.

**Decision gate:** Reuse the public `RobertMagowan/ordersApp` repository and current protected environments. Prompt for test/production deployment identities, alert/action-group owners, budgets, and any new environment reviewer requirement.

**Files:** `.github/workflows/{ci,infrastructure,deploy-apps,e2e,scheduled-e2e,load}.yml`, release manifest generator, `ops/runbooks/*`, budgets/alerts Bicep, Dependabot/security configuration, and README deployment docs.

**Tasks:**

- [ ] CI: restore, format, build, unit/integration/contract/bUnit, OpenAPI diff, WASM publish, container/IaC/dependency/secret scans, and the local Playwright subset. Pin every non-local action and reusable workflow used by CI or deployment to a verified full-length commit SHA with a readable version comment, enforce the rule automatically, and allow an exception only when its rationale, owner, risk review, and expiry are documented and approved.
- [ ] Infrastructure workflow: OIDC login, Bicep build/validate/what-if, protected deployment, branch-safe manual dispatch, concurrency, evidence retention, and deployed endpoint/change summary in `$GITHUB_STEP_SUMMARY`.
- [ ] Application workflow: trigger the managed ACR Task build through the approved trusted-services contract, run the Azure-native deployment/migration Job, deploy the WASM artifact, smoke-test, run protected E2E, and publish one release manifest containing every immutable digest/hash. Promote the API by server-side ACR import through the same contract and an environment-scoped deployment identity; do not add another public path and never rebuild between environments.
- [ ] Add rollback/runbooks for DLQ replay, stuck outbox, failed migration, identity/network failure, telemetry silence, restore, and release rollback.
- [ ] Add NBomber test-environment workloads with cost guardrails, dependency/queue protection, and published results/metrics.

**Manual test:** Open a PR and verify unprivileged checks; merge to development and verify immutable artifact deployment, smoke, E2E, telemetry, summary/evidence, and rollback; promote the same manifest through a development → test PR.

**Deploy gate:** `ordersApp` can build and promote the same trusted artifacts through development and test without long-lived Azure secrets. Commit `ci: add protected artifact promotion`.

**Natural commit checkpoints:** CI/security checks; infrastructure OIDC workflow; application artifact workflow; release manifest/promotion; runbooks/alerts; test-environment load evidence.

## Sprint 15 — Production Readiness

**Estimated effort:** 5–8 implementation days + 3 development-verification days + 1–2 QA days = 9–13 working days.

**Outcome:** The system is supportable, secure, cost-controlled, and ready for a reviewed production promotion.

**Tasks:**

- [ ] Run the complete Release, SQL migration-upgrade, contract, unit, integration, bUnit, Playwright, accessibility, security, load, restore, and rollback matrix from a clean checkout.
- [ ] Verify private networking, TLS, API edge/linking, CORS, rate limits, request limits, identity negative tests, backup/retention, budgets, quotas, tags, and region support.
- [ ] Remove bootstrap firewall rules, credentials, unused resources, development fault hooks, TestSupport production paths, and obsolete documentation.
- [ ] Have an independent operator execute the runbooks and record evidence under `docs/evidence/sprint-15/`; if unavailable, record the solo rehearsal and explicitly accept the residual operational risk before production.
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

- **Spec coverage:** repository/promotion foundations (S0–0.6), delivered domain/API baseline (S1), delivered workflow/test assurance (S2), delivered SQL durability (S3), authenticated profile ownership (S4A), history and schema-contract completion (S4B), outbox/inbox and Azure messaging (S5), local reproducibility (S6), Azure network/RBAC hardening (S7), Flex hosting (S8), web edge/authentication (S9), frontend foundation (S10), order workflows (S11), frontend quality (S12), TestSupport/observability (S13), CI/CD/operations (S14), and production readiness (S15) are each assigned.
- **Assurance coverage:** every remaining sprint has focused implementation commits, three independent development-verification days selected by technology/risk, one to two QA days in Azure `test`, evidence retention, and a fresh-feature-branch defect loop. QA-only agents do not edit implementation.
- **Frontend scope correction:** the former single web sprint is split into four independently reviewable increments. S9 proves hosting/auth/API connectivity, S10 proves the accessible design system, S11 proves business workflows, and S12 proves cross-browser quality and rollback. The Observability Lab remains in S13 with its safety API.
- **Azure deployability correction:** every delivered feature sprint now ends with an Azure development deployment or regression deployment. S2 creates the minimal protected test-environment gate, S3 introduces Azure SQL, S5 introduces minimum Service Bus/Function hosting, and S7 hardens those already-working resources rather than postponing all cloud testing.
- **Sequence check:** S4A establishes identity before any customer traffic; its transition occurs under quiescence before D1 traffic, and Sprint 3 is never a security rollback. S4B first enforces ownership (R2), then adds history (H1), then bridges/contracts idempotency (R3/R4) with an explicit 14-day D2 soak. Each frontend sprint consumes the stable S4A/S4B contracts; TestSupport follows the business UI; release automation follows all deployable artifacts. No later sprint is required to make an earlier sprint's stated manual journey possible.
- **Placeholder scan:** no unowned placeholder implementation steps remain. Tenant/app IDs, test users, production domain, budgets, alert owners, and production approval are explicit user decision gates because they cannot be safely inferred.
- **Type/interface consistency:** `OrderCreatedIntegrationEventV1`, `OutboxDispatcher.DrainAsync(CancellationToken)`, `OrderProcessor.ProcessAsync(ServiceBusReceivedMessage, CancellationToken)`, `IIdempotencyStore`, and `/api/v1` retain their semantics. The UI explicitly maps API `pending` to the user-facing `Received` label.
- **Security and operations:** browser secrets, automatic non-idempotent POST retry, self-hosted GitHub runners for this public repository, development identities in production, unrestricted public data services after Sprint 7, mutable artifacts, and production TestSupport are prohibited and have negative-test gates. Any temporary or managed-service network exception is explicit, least scoped, monitored, regression tested, and has a service-tag drift/removal path where applicable.
- **Effort review:** 133–184 remaining working days on the reset path, or 136–188 on the mapped-backfill path (158–220 / 161–224 including delivered historical work), is a planning range for one developer, not elapsed calendar time. It includes independent assurance gates, excludes defect remediation/external delays, and excludes the elapsed D2 soak. Re-estimate at each sprint boundary using measured throughput and newly discovered Azure constraints.
