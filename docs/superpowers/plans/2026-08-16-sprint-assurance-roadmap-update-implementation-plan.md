# Sprint Assurance Roadmap Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` (inline) or `superpowers:subagent-driven-development` (one fresh worker per task) to execute this plan task-by-task. Steps use checkbox syntax for tracking.

> **Historical roadmap-update plan.** The project-wide roadmap now supersedes its authorization/history split with Sprint 4A (External ID identity/ownership) and Sprint 4B (history/schema-contract completion). Use `2026-08-16-cloudorders-sprint-implementation-plan.md` for all current estimates and sequencing.

**Goal:** Re-baseline the remaining CloudOrders roadmap so every sprint is independently deployable, verified in Azure development, and independently QA-tested in Azure test.

**Architecture:** Preserve delivered Sprints 0–1. Split the former broad Sprint 2 into workflow/contract/test-environment assurance, SQL durability, and API authorization/customer history; shift later sprint numbers without changing their technical dependency order. A common gate defines the evidence, roles, and defect loop for each remaining sprint.

**Tech Stack:** Markdown, GitHub Actions, Azure environments, .NET 10, Bicep, xUnit, Testcontainers, Playwright, bUnit, NBomber.

## Global Constraints

- Use `feature/*` branches and promote only through `development`, `test`, then `master` by pull request.
- Retain three working days of independent development-environment verification and one to two working days of QA in Azure test for every remaining sprint.
- Record release IDs, deployment URLs, commands, results, defects, and re-test evidence under `docs/evidence/sprint-<number>/`.
- Fix QA defects on a new `feature/*` branch and re-run the affected gates; do not promote unresolved defects.

---

### Task 1: Add the reusable assurance model

**Files:**
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`
- Modify: `AGENTS.md`

- [x] Add a delivery lifecycle that separates developer implementation, independent three-day development verification, independent QA in `test`, and promotion.
- [x] State the evidence location, technology-specific test-selection rule, and fresh-branch defect loop.
- [x] Run `rg -n "three working days|QA-only|docs/evidence"` against both files and confirm all terms are consistent.
- [x] Commit with `docs: define sprint assurance gates`.

### Task 2: Split and re-estimate the remaining roadmap

**Files:**
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`

- [x] Replace the former Sprint 2 with Workflow, Contract, and Test-Environment Assurance (2–3 implementation days, three development-verification days, and one to two QA days).
- [x] Create separate SQL durability and API authorization/customer-history sprints; renumber later work while preserving dependency order.
- [x] Add an explicit assurance gate and test type to every remaining sprint, and recalculate total estimates before defect remediation.
- [x] Run `rg -n "## Sprint|Estimated effort|S[0-9]"` and manually verify unique ordered sprint numbers and correct references.
- [x] Commit with `docs: split remaining delivery sprints`.

### Task 3: Align supporting specifications and contributor policy

**Files:**
- Modify: `docs/superpowers/specs/2026-08-16-cloudorders-greenfield-design.md`
- Modify: `docs/superpowers/specs/2026-08-16-frontend-sprint-replan-design.md`
- Modify: `AGENTS.md`

- [x] Update delivery sequencing and frontend sprint references after the split.
- [x] Ensure contributor guidance requires independent development verification, QA in test, evidence, and a fresh defect branch.
- [x] Run `git diff --check` and review all numbered cross-references.
- [x] Commit with `docs: align roadmap assurance references`.

### Task 4: Review the revised roadmap

**Files:**
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`

- [x] Check each approved design requirement is assigned to a sprint and no sprint relies on a later prerequisite.
- [x] Search for stale sprint numbers, old effort totals, self-hosted-runner terminology, and insecure deployment instructions; correct each finding.
- [x] Run `git diff --check` and `dotnet test --configuration Release`.
- [x] Add the review outcome to the roadmap self-review and commit with `docs: review sprint assurance roadmap`.
