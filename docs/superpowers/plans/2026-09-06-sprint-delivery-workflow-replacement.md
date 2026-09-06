# Sprint Delivery Workflow Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the version-1 delivery orchestrator with a durable, reconciled workflow that safely resumes the real Sprint 4A state.

**Architecture:** Retain repository CI/CD and Azure deployment tooling as deterministic executors. Replace the delivery configuration, state schema, reconciliation command, tests, role runbook, and evidence with versioned policy that separates lifecycle, PR/review status, gates, acceptance criteria, and immutable artifacts.

**Tech Stack:** PowerShell 5.1-compatible scripts, Pester, JSON, Git/GitHub/Azure read-only snapshots, Markdown.

## Global Constraints

- Freeze product progression until `WORKFLOW_CUTOVER_COMPLETE` is recorded.
- Never delete worktrees/branches, alter existing PRs, deploy, migrate, or change production during migration.
- Reconcile Git, GitHub, and Azure facts before lifecycle side effects; fail closed on contradictions.
- Keep PR-only `feature/*` → `development` → `test` → `master` promotion and environment approvals.

---

### Task 1: Replace durable policy and state schema

**Files:**
- Modify: `delivery/config.json`
- Modify: `delivery/state.json`
- Create: `delivery/evidence/workflow-replacement-baseline.json`

- [ ] Define version `2.0` vocabulary for work lifecycle, PR lifecycle, review status, orchestration stages, evidence/gate states, acceptance verification stages, retry/escalation policy, and explicit human statuses.
- [ ] Reconstruct Sprint 4A from authoritative current facts: E1 is test-validated at commit `2b72a2f8d089d0d512fb99f6a2ef5cd3aeea4580`; unfinished work retains its documented status and D1 remains blocked.
- [ ] Record current Git/PR/CI/Azure evidence as immutable evidence bindings; classify legacy evidence as historical or stale rather than overwriting it.
- [ ] Commit: `feat: replace delivery workflow state schema`.

### Task 2: Replace reconciliation and transition validation

**Files:**
- Modify: `ops/Invoke-SprintDelivery.ps1`
- Modify: `ops/Test-SprintDelivery.ps1`
- Modify: `ops/tests/SprintDelivery.Tests.ps1`

- [ ] Write failing Pester coverage proving state-version rejection, separate PR/review lifecycles, SHA-staleness, acceptance-stage ordering, duplicate-side-effect prevention, and inconclusive authoritative snapshots.
- [ ] Implement read-only reconciliation and transition derivation with explicit `HUMAN_DECISION_REQUIRED` and `HUMAN_REVIEW_REQUIRED` outcomes; retain PowerShell 5.1 compatibility.
- [ ] Run focused Pester tests, then the full delivery test suite; commit `feat: enforce workflow reconciliation and gates`.

### Task 3: Replace operator/agent guidance and cut over

**Files:**
- Modify: `AGENTS.md`
- Modify: `docs/operations/sprint-delivery-workflow.md`
- Modify: `docs/evidence/delivery-workflow-migration.md`
- Create: `docs/operations/sprint-delivery-bootstrap.md`

- [ ] Document the supplied bootstrap workflow as the replacement authority and the concise normal-resume command for future sessions.
- [ ] Record the migration report, KEEP/ADAPT/REPLACE classification, imported/stale evidence, current Sprint 4A items, and cutover result.
- [ ] Run JSON parsing, Pester, `git diff --check`, and a read-only reconciliation dry run. Commit `docs: cut over sprint delivery workflow`.

### Task 4: Review and integrate the migration

**Files:**
- Review: all Task 1–3 files

- [ ] Run formatting/build/tests affected by the migration and manually review policy against the approved design.
- [ ] Create a PR to `development`; do not merge until its current-SHA CI and review evidence pass.
