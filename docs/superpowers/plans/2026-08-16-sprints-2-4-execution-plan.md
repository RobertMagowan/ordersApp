# CloudOrders Sprints 2–4 Execution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` task-by-task. Tasks use checkbox syntax for tracking.

> **Historical plan — partially superseded.** Sprints 2 and 3 are complete. Its Sprint 4 group-to-customer design is superseded by the External ID `CustomerProfile` plan and the project-wide Sprint 4A/4B roadmap in `2026-08-16-cloudorders-sprint-implementation-plan.md`; do not execute its Sprint 4 tasks.

**Goal:** Deliver secure promotion/test-environment workflow controls, durable SQL idempotency, and Entra group-to-customer authorization with review, development verification, and Azure test QA evidence.

**Architecture:** Retain the existing minimal API vertical slice while replacing in-memory persistence with EF Core SQL transactions for orders, idempotency records, and outbox messages. Authentication uses Entra bearer tokens; authorization maps configured Entra group object IDs to allowed customer references, with no customer claim trusted from the client.

**Tech Stack:** .NET 10/C# 14, ASP.NET Core minimal APIs, EF Core 10/SQL Server, Testcontainers, Bicep, GitHub Actions OIDC, Microsoft Entra ID.

## Global Constraints

- Work only on `feature/replan-sprint-assurance`; promote through pull requests only.
- Do not call `Database.Migrate()` from application startup, publish directly to Service Bus, commit secrets, or weaken TLS validation.
- The existing `ordersapp-test` resource group in `ukwest` is authorized for non-production spend; the logged-in tenant/subscription are the approved targets.
- Map Entra group object IDs to customer references through validated configuration. Return `404` for unauthorized customer resources.
- Before independent review, a separate high-capability agent performs three working days of thorough developer-style local verification using technology-appropriate tests and direct inspection of affected state: SQL rows/migration history, outbox and idempotency records, local configuration, workflow artifacts, and authorization mappings as applicable. Record the commands and sanitized results with sprint evidence.
- After review and initial deployment to Azure `development`, a dedicated smoke-test agent must validate the live release and record its result.
- Once the reviewed release merges to `test`, a QA-only agent performs adversarial feature testing in Azure `test`: successful, boundary, invalid, authorization, failure/recovery, state-integrity, concurrency, and regression paths appropriate to the technology. A defect blocks onward promotion and follows the fresh-branch remediation loop.
- Every task follows TDD, has a focused review, and is committed. QA defects use a new `feature/*` branch.

---

### Task 1: Harden the promotion workflow and add the contract pack

**Files:**
- Modify: `.github/workflows/deploy.yml`, `README.md`, `AGENTS.md`
- Create: `docs/contracts/{frontend-design.md,v1-contracts.md,traceability.md}`

- [ ] Write policy/contract tests or deterministic validation scripts before changing workflow behavior.
- [ ] Replace unpinned action tags with verified full commit SHAs and Node 24-compatible action releases; restrict manual dispatch to promotion branches; remove insecure curl; emit endpoint, release, and immutable artifact data in the workflow summary.
- [ ] Version handoff section 19 and sections 25–35 into repository-owned contract documents and map requirements to projects/tests/sprints.
- [ ] Run workflow/Bicep validation plus the full .NET test suite; commit focused workflow and contract changes separately.

### Task 2: Provision and prove the protected Azure test release path

**Files:**
- Modify: `infra/main.bicep`, `infra/environments/test.bicepparam`, `.github/workflows/deploy.yml`, `README.md`
- Create: `docs/evidence/sprint-2/{development-verification.md,test-qa.md,rollback.md}`

- [ ] Add only the Bicep/workflow inputs needed to deploy the immutable API release into `ordersapp-test` with the existing naming overlay.
- [ ] Configure or validate the GitHub `test` environment OIDC values and branch restriction; do not add long-lived credentials.
- [ ] Run Bicep build/lint/parameter validation and reviewed what-if; deploy the development release, promote it through a `development` → `test` PR, smoke-test both, and record immutable identifiers and rollback evidence.
- [ ] Run three days’ equivalent technology-appropriate development verification and QA-only Azure test checks; record results and defects.

### Task 3: Add SQL persistence, migrations, atomic outbox, and durable idempotency

**Files:**
- Modify: `src/CloudOrders.{Application,Infrastructure,Api}/**`, `Directory.Packages.props`, solution/projects
- Create: EF Core models/configurations/migrations, SQL repositories/idempotency store, Testcontainers integration tests, local SQL Compose/service documentation

- [ ] Write failing integration tests for SQL create/read, first-use, exact replay, payload conflict, concurrent requests, and transactionally persisted outbox records.
- [ ] Implement the minimal EF Core model and explicit migration; create one transaction for Order, IdempotencyRecord, and OutboxMessage.
- [ ] Implement UUID `Idempotency-Key` behavior with canonical payload hashing and `201`/`200` replay/`409` conflict responses; use a non-production deterministic subject abstraction until Entra binding is added in Task 5.
- [ ] Add explicit deployment migration commands, SQL health checks, API limits/timeout/unknown-member validation, and local SQL configuration; never migrate at startup.
- [ ] Run focused integration tests, full suite, container/local manual flow, development deployment verification, then test-environment QA; commit at red/green/schema/API boundaries.

### Task 4: Add Entra bearer authorization and customer history

**Files:**
- Modify: `src/CloudOrders.{Api,Application,Infrastructure,Contracts}/**`, `infra/**`, `.github/workflows/deploy.yml`
- Create: authorization options/configuration, group-customer mapping tests, history DTOs/cursor tests, Entra setup/runbook evidence under `docs/evidence/sprint-4/`

- [ ] Write failing tests for denied tokens, malformed `oid`, group/customer mapping, cross-customer `404`, and stable newest-first cursor history.
- [ ] Implement JWT bearer validation, read/write policies, configuration validation, Entra `oid` idempotency binding, and group-object-ID-to-customer authorization.
- [ ] Implement authorized customer history with `(CreatedAtUtc, Id)` cursor ordering and page sizes 1–100.
- [ ] Create/configure non-production Entra API registration, test group(s), group claims, and membership for the approved tenant; never commit IDs or secrets.
- [ ] Run development authorization-negative/history/replay verification, independent Azure test QA, fix discovered defects from a fresh branch, and retain evidence.

### Task 5: Whole-branch review and sprint closure

**Files:**
- Create: `docs/evidence/sprint-{2,3,4}/review.md`
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`

- [ ] Perform a broad code/architecture/security review of the complete branch diff.
- [ ] Fix all Critical and Important findings on fresh `feature/*` branches, with focused re-tests and re-review.
- [ ] Verify .NET, Bicep, workflow, development, and test-environment evidence; update roadmap status only when all gates pass.
