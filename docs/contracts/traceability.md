# Contract traceability map

**Contract version:** 1.0.0
**Sprint gate:** This map is updated with the contract pack and reviewed in every pull request that changes a mapped requirement.

The map connects the source-backed contracts to repository ownership, automated verification, and delivery gates. `Planned` identifies the sprint that must add the named project/test before the contract can be claimed as delivered; it is not a substitute for a test.

| Contract requirement | Repository owner | Automated verification | Sprint gate |
|---|---|---|---|
| Section 19: standalone Blazor WASM routes, dispatch-control visual language, business-route semantics, accessibility | `src/CloudOrders.Web`, `src/CloudOrders.Api.Client` | `tests/CloudOrders.Web.Tests` bUnit accessibility/component tests; `tests/CloudOrders.Playwright` browser coverage (planned) | Sprint 10 foundation; Sprint 11 workflow; Sprint 12 quality |
| Section 19: PKCE, no browser secrets, exact CORS, TestOperator diagnostics | `src/CloudOrders.Web`, `src/CloudOrders.Api`, `infra/` | authorization-negative API tests, bUnit authorization rendering, Playwright role tests (planned) | Sprint 4 authorization; Sprint 9 web edge/authentication |
| Section 25: Pending → Processing only, EventId-aware idempotent reprocessing | `src/CloudOrders.Domain`, `src/CloudOrders.Application` | `tests/CloudOrders.UnitTests/OrderTests.cs`; processor tests (planned) | Sprint 1 baseline; Sprint 5 messaging |
| Section 26: `/api/v1` order API, validation, Problem Details, compatibility | `src/CloudOrders.Api`, `src/CloudOrders.Contracts`, `src/CloudOrders.Api.Client` | `tests/CloudOrders.IntegrationTests`; OpenAPI compatibility test (planned) | Sprint 1 baseline; Sprint 3 idempotency; Sprint 4 customer API |
| Section 26: durable Idempotency-Key replay/conflict, authorized customer scope, cursor | `src/CloudOrders.Api`, `src/CloudOrders.Application`, `src/CloudOrders.Infrastructure` | SQL integration/concurrency/authorization-negative tests (planned) | Sprint 3 SQL/idempotency; Sprint 4 authorization/history |
| Section 27: Orders/Outbox/Inbox/Idempotency schema and atomic transaction | `src/CloudOrders.Infrastructure`, `infra/` | migration build/upgrade, SQL integration and concurrency tests (planned) | Sprint 3 |
| Section 27: TestSupport leases and bounded retention | `src/CloudOrders.TestSupport.Api`, `infra/`, `ops/` | TestSupport allowlist/lease/cleanup tests (planned) | Sprint 13 |
| Section 28: SWA/API edge, private dependencies, per-workload managed identities | `infra/`, `src/CloudOrders.Web`, `src/CloudOrders.Api` | Bicep build/lint/parameters/what-if and RBAC-negative tests (planned) | Sprint 7 security/network; Sprint 9 web edge |
| Section 28: immutable Flex Functions publisher/processor and settlement rules | `src/CloudOrders.OutboxPublisher`, `src/CloudOrders.OrderProcessor`, `infra/` | emulator/Testcontainers service-flow and deployment tests (planned) | Sprint 5 messaging; Sprint 8 Functions hosting |
| Section 29: exact telemetry fields/events, KQL, dashboards, alerts | `src/**`, `ops/kql`, `ops/` | structured-log/trace contract tests and KQL validation (planned) | Sprint 13 |
| Section 30: scenario-to-evidence matrix and telemetry completeness | `tests/CloudOrders.Playwright`, `docs/evidence/` | Playwright scenario/telemetry assertions (planned) | Sprint 12; Sprint 13 |
| Section 31: latency, throughput, backlog, browser payload budgets | `tests/CloudOrders.LoadTests`, `tests/CloudOrders.Playwright` | NBomber workloads and browser-budget tests (planned) | Sprint 12; Sprint 13 |
| Section 32: Entra roles, data classification, OIDC supply-chain controls | `src/CloudOrders.Api`, `infra/`, `.github/workflows/`, `docs/` | authorization/RBAC negatives; action-pinning policy tests; secret/dependency/IaC scans (planned) | Sprint 2 workflow assurance; Sprint 4 authorization; Sprint 7 security |
| Section 33: same immutable artifact, expand/migrate/contract, manifest/rollback | `.github/workflows/deploy.yml`, `infra/`, `ops/runbooks/` | workflow policy tests, Bicep validation, deployment/rollback evidence (current policy test; further tests planned) | Sprint 2 workflow; Sprint 3 migration; Sprint 14 release automation |
| Section 34: DLQ, outbox, migration, identity, telemetry, restore, rollback runbooks | `ops/runbooks/`, `docs/evidence/` | runbook rehearsal and evidence checks (planned) | Sprint 5 messaging; Sprint 13 observability; Sprint 14 operations |
| Section 35: complete v1 gate | all product, infra, tests, and evidence | Release matrix and acceptance audit (planned) | Sprint 15 |
| Workflow: Actions are Node 24 releases pinned to full reviewed SHAs; manual dispatch is promotion-only; TLS is verified; summary has release/artifact/endpoint | `.github/workflows/deploy.yml`, `tests/CloudOrders.ArchitectureTests` | `RepositoryPolicyTests.DeploymentWorkflowEnforcesPinnedPromotionAndReleasePolicy` | Sprint 2 Task 1 |
| Contract pack is repository-owned and versioned | `docs/contracts/`, `tests/CloudOrders.ArchitectureTests` | `RepositoryPolicyTests.RepositoryContainsVersionedContractPackAndTraceability` | Sprint 2 Task 1 |

## Evidence ownership

Developer implementation evidence, automated output, deployment URLs, immutable release IDs, independent development verification, QA-only test validation, defects, and retests are retained under `docs/evidence/sprint-<number>/`. A defect correction starts on a fresh `feature/*` branch and repeats the affected development/test promotion gates before `test` → `master` may proceed.
