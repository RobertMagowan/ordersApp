# Sprint Delivery Workflow Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install one resumable, repository-native sprint-delivery workflow without restarting or risking existing CloudOrders Sprint 4A work.

**Architecture:** A small PowerShell orchestrator validates versioned JSON configuration/state/evidence against JSON Schemas, reconciles state against Git and GitHub before any side effect, and delegates deterministic operations to existing commands and workflows. Repository skills provide focused reasoning roles; GitHub Actions, Bicep, Azure, and test tools remain the execution authorities.

**Tech Stack:** PowerShell 7, Pester 5, JSON Schema draft 2020-12, Git, GitHub CLI/API, .NET 10, GitHub Actions, Bicep.

## Global Constraints

- Work only from `feature/sprint-delivery-workflow`; preserve all existing worktrees and uncommitted files.
- Do not push, merge, deploy, alter Azure/Entra, delete branches/worktrees, or mutate production during this migration.
- Keep `feature/*` → `development` → `test` → `master`, GitHub branch protection, CI, Bicep validation, and protected deployment workflows.
- Store no secrets, tokens, connection strings, real customer data, or GitHub environment values in delivery state/evidence.
- Treat Git as authoritative for commit/branch state, GitHub for PR/CI, Azure for deployments/artifacts, and workflow state only for orchestration history and expected progression.
- Treat the current Sprint 4A plan and versioned contract pack as the current requirements authority; mark superseded planning content historical instead of deleting it.
- Require explicit human decisions for destructive data transition, production, incompatible requirements, privileged identity/permission change, or inaccessible credentials.

## File Structure

| Path | Responsibility |
| --- | --- |
| `delivery/config.json` | Stable repository policy and canonical operations. |
| `delivery/state.json` | Current sprint/work-item lifecycle, stages, gate statuses, evidence links, blockers, and baselines. |
| `delivery/schemas/*.schema.json` | Deterministic validation of configuration, state, and evidence. |
| `delivery/evidence/*.json` | Compact, commit-bound baseline/reconciliation/cutover evidence. |
| `ops/Invoke-SprintDelivery.ps1` | Sole orchestration command; performs no hidden deployment or migration. |
| `ops/Test-SprintDelivery.ps1` | Schema, transition, reconciliation, and safety test runner. |
| `ops/tests/SprintDelivery.Tests.ps1` | Pester coverage for all state transitions and safety cases. |
| `.agents/skills/*/SKILL.md` | Focused reusable role instructions, not duplicated operating scripts. |
| `docs/operations/sprint-delivery-workflow.md` | Fresh-session resume, evidence, human gates, and recovery guide. |
| `docs/evidence/delivery-workflow-migration.md` | Required migration report and imported-evidence inventory. |

---

### Task 1: Define versioned delivery contracts and state transition model

**Files:**
- Create: `delivery/schemas/config.schema.json`
- Create: `delivery/schemas/state.schema.json`
- Create: `delivery/schemas/evidence.schema.json`
- Create: `delivery/config.json`
- Create: `delivery/state.json`
- Test: `ops/tests/SprintDelivery.Tests.ps1`

**Consumes:** [workflow design](../specs/2026-09-05-sprint-delivery-workflow-design.md), `AGENTS.md`, current Sprint 4 plan.

**Produces:** `workflowVersion`, `stateSchemaVersion`, `configurationVersion`, logical lifecycle/stage/gate vocabulary, and a valid imported Sprint 4A state.

- [ ] **Step 1: Write failing schema and transition tests.**

```powershell
Describe 'Sprint delivery contracts' {
    It 'rejects a state with a lifecycle/gate collision' {
        $state = Get-DeliveryFixture 'merged-with-failed-ci'
        { Test-SprintDeliveryState -State $state -Config $config } | Should -Throw '*lifecycle*gate*'
    }

    It 'rejects completion while a required gate is pending' {
        $state = Get-DeliveryFixture 'task-done-with-pending-review'
        { Assert-TaskDone -WorkItem $state.currentSprint.workItems[0] -Config $config } |
            Should -Throw '*codeReview*'
    }
}
```

- [ ] **Step 2: Run the focused test to prove it fails.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag contracts`

Expected: FAIL because state/schema functions and fixtures do not exist.

- [ ] **Step 3: Add schemas and policy.** Define enum-only lifecycle (`TODO`, `IN_PROGRESS`, `PR_OPEN`, `MERGED`, `DEV_DEPLOYED`, `QA_DEPLOYED`, `RELEASED`, `CANCELLED`, `SUPERSEDED`), stages, gate values, version fields, evidence bindings, retry counters, blockers, and `HUMAN_DECISION_REQUIRED`. Configure current branch promotion, canonical commands, required gates by risk, `default_model`/`strong_model` logical roles, and bounded retry limits.

- [ ] **Step 4: Add the initial imported state.** Record `sprintPlanSource`, commit `fbc68a9f0e02923880c8a06162a8d7cda2afac38`, current Sprint 4A, Task 1/2/3/6 merged evidence, E1 development deployment run `33457927112`, Task 4/5 pending, Task 7 D1 blocked by documented identity/data-transition decisions, and every unknown/historical gate explicitly.

- [ ] **Step 5: Run contract tests to prove they pass.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag contracts`

Expected: PASS with invalid state rejected and the imported state accepted.

- [ ] **Step 6: Commit the contracts.**

```powershell
git add delivery/schemas delivery/config.json delivery/state.json ops/tests/SprintDelivery.Tests.ps1
git commit -m "feat: define sprint delivery state contracts"
```

### Task 2: Implement deterministic validation and derived completion

**Files:**
- Create: `ops/Invoke-SprintDelivery.ps1`
- Create: `ops/Test-SprintDelivery.ps1`
- Modify: `ops/tests/SprintDelivery.Tests.ps1`
- Test: `ops/tests/SprintDelivery.Tests.ps1`

**Consumes:** Task 1 schemas/config/state.

**Produces:** `Test-SprintDeliveryState`, `Assert-TaskDone`, `Get-NextDeliveryAction`, and `-WhatIf`/read-only command modes.

- [ ] **Step 1: Write failing derived-completion tests.**

```powershell
It 'derives task completion instead of accepting a declared done label' {
    $item = Get-DeliveryFixture 'all-required-gates-pass'.currentSprint.workItems[0]
    (Assert-TaskDone -WorkItem $item -Config $config) | Should -BeTrue
    $item.gates.devValidation.status = 'STALE'
    { Assert-TaskDone -WorkItem $item -Config $config } | Should -Throw '*STALE*'
}
```

- [ ] **Step 2: Run the focused test.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag completion`

Expected: FAIL because derived completion is absent.

- [ ] **Step 3: Implement minimal functions.** Load JSON with `ConvertFrom-Json -AsHashtable`, validate required versions/schema shape, compute required gates from risk/config, and return a structured next action without calling GitHub/Azure unless an explicit `-Execute` switch is supplied. Default mode must be read-only and print `HUMAN_DECISION_REQUIRED` rather than improvising a restricted action.

- [ ] **Step 4: Add command help and deterministic test entry point.** `ops/Test-SprintDelivery.ps1` invokes only the delivery Pester file and returns the Pester exit code; `Invoke-SprintDelivery.ps1 -WhatIf` never changes Git, GitHub, Azure, files, or environments.

- [ ] **Step 5: Verify.**

Run: `pwsh -File ops/Test-SprintDelivery.ps1; pwsh -File ops/Invoke-SprintDelivery.ps1 -WhatIf`

Expected: PASS; output identifies Sprint 4A, active work items, pending gates, and no side effect.

- [ ] **Step 6: Commit.**

```powershell
git add ops/Invoke-SprintDelivery.ps1 ops/Test-SprintDelivery.ps1 ops/tests/SprintDelivery.Tests.ps1
git commit -m "feat: add deterministic sprint delivery validation"
```

### Task 3: Add reconciliation, evidence invalidation, and idempotency guards

**Files:**
- Modify: `ops/Invoke-SprintDelivery.ps1`
- Modify: `ops/tests/SprintDelivery.Tests.ps1`
- Create: `delivery/evidence/pre-migration-baseline.json`
- Create: `delivery/evidence/reconciliation.json`

**Consumes:** Task 2 commands and Task 1 state.

**Produces:** `Get-AuthoritativeSnapshot`, `Compare-DeliveryState`, `Invalidate-DependentEvidence`, and `Assert-SideEffectNotDuplicate`.

- [ ] **Step 1: Write failing reconciliation tests.**

```powershell
It 'marks deployment evidence stale when its commit differs from Azure evidence' {
    $result = Compare-DeliveryState -State $state -Snapshot @{ deployments = @(@{ commit = 'other' }) }
    $result.state.currentSprint.workItems[0].gates.devValidation.status | Should -Be 'STALE'
}

It 'refuses a duplicate migration execution before invoking any command' {
    { Assert-SideEffectNotDuplicate -Kind migration -Identity 'E1:fbc68a9' -State $state } |
        Should -Throw '*already recorded*'
}
```

- [ ] **Step 2: Run reconciliation tests.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag reconciliation`

Expected: FAIL because authoritative comparison and duplicate guards are absent.

- [ ] **Step 3: Implement read-only reconciliation.** Query only Git (`git status`, refs, worktrees), GitHub (`gh pr`, `gh run`), and configured Azure read APIs. Compare exact commit/run/artifact/environment identifiers; never overwrite facts with local state. Record contradictions as `STATE_RECONCILIATION_REQUIRED`.

- [ ] **Step 4: Record imported baseline evidence.** Include the commands/results already observed: restore/format/build pass, architecture 24/24, unit 13/13, Bicep checks pass, and integration 47/73 with 26 Docker-unavailable failures classified `ENVIRONMENT_FAILURE`. Bind it to `5c0e1ab136f477127ce194426742a27d704d20d4` and mark it historical for later commits.

- [ ] **Step 5: Verify no mutation.**

Run: `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

Expected: reports GitHub/Azure divergence, stale evidence, or current agreement without creating branches, PRs, deployments, migrations, or state changes.

- [ ] **Step 6: Commit.**

```powershell
git add ops/Invoke-SprintDelivery.ps1 ops/tests/SprintDelivery.Tests.ps1 delivery/evidence
git commit -m "feat: reconcile sprint delivery evidence"
```

### Task 4: Install focused repository skills and operational documentation

**Files:**
- Create: `.agents/skills/sprint-orchestrator/SKILL.md`
- Create: `.agents/skills/task-planning/SKILL.md`
- Create: `.agents/skills/task-implementation/SKILL.md`
- Create: `.agents/skills/automated-testing/SKILL.md`
- Create: `.agents/skills/failure-resolution/SKILL.md`
- Create: `.agents/skills/environment-validation/SKILL.md`
- Create: `.agents/skills/qa/SKILL.md`
- Create: `docs/operations/sprint-delivery-workflow.md`
- Modify: `AGENTS.md`
- Test: `ops/tests/SprintDelivery.Tests.ps1`

**Consumes:** Tasks 1–3.

**Produces:** One coordination authority, reusable role instructions, and a fresh-session runbook.

- [ ] **Step 1: Write failing content-policy tests.** Assert every skill names its inputs/outputs, no skill embeds tenant IDs/model names/secrets, only `sprint-orchestrator` may advance lifecycle state, and `AGENTS.md` links to—not duplicates—the workflow.

- [ ] **Step 2: Run policy tests.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag skill-policy`

Expected: FAIL because skills and links do not exist.

- [ ] **Step 3: Add concise skills.** Require compact handoffs, plan/review separation, TDD, direct state verification, independent QA, failure classification, bounded escalation, and no production/destructive side effects without authority. Skills call `Invoke-SprintDelivery.ps1`; they do not create competing state machines.

- [ ] **Step 4: Document resume and recovery.** Include `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile`, evidence fields, stale-state repair, cancellation/supersession, Docker environment-failure classification, and the precise human-decision boundaries.

- [ ] **Step 5: Verify.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag skill-policy`

Expected: PASS.

- [ ] **Step 6: Commit.**

```powershell
git add .agents/skills docs/operations/sprint-delivery-workflow.md AGENTS.md ops/tests/SprintDelivery.Tests.ps1
git commit -m "docs: add reusable sprint delivery roles"
```

### Task 5: Add safe CI enforcement and PR evidence template

**Files:**
- Create: `.github/workflows/sprint-delivery-validation.yml`
- Modify: `.github/pull_request_template.md`
- Modify: `ops/tests/SprintDelivery.Tests.ps1`
- Test: `ops/tests/SprintDelivery.Tests.ps1`

**Consumes:** schema/config/test entry point from Tasks 1–3.

**Produces:** CI validation of delivery contracts without CI changing workflow state or Azure.

- [ ] **Step 1: Write failing workflow-policy tests.** Assert workflow runs on pull requests affecting `delivery/**`, `ops/**`, `.agents/skills/**`, `AGENTS.md`, or workflow files; calls `pwsh -File ops/Test-SprintDelivery.ps1`; has read-only permissions; and does not run Azure login/deployment/migration commands.

- [ ] **Step 2: Run the focused policy test.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag ci-policy`

Expected: FAIL because the validation workflow is absent.

- [ ] **Step 3: Add the read-only workflow.** Pin checkout/setup actions to full SHA values consistent with repository policy, restore only Pester if the repository lacks it, run schema/transition tests, and upload no secrets or live environment state. Add PR template fields for state/evidence impact and explicit gate status.

- [ ] **Step 4: Verify local workflow structure.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag ci-policy`

Expected: PASS; static check proves no privileged/cloud mutation command exists.

- [ ] **Step 5: Commit.**

```powershell
git add .github/workflows/sprint-delivery-validation.yml .github/pull_request_template.md ops/tests/SprintDelivery.Tests.ps1
git commit -m "ci: validate sprint delivery workflow"
```

### Task 6: Migrate Sprint 4A state and prove safe cutover

**Files:**
- Modify: `delivery/state.json`
- Create: `delivery/evidence/cutover-validation.json`
- Create: `docs/evidence/delivery-workflow-migration.md`
- Modify: `ops/tests/SprintDelivery.Tests.ps1`
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`
- Test: `ops/tests/SprintDelivery.Tests.ps1`

**Consumes:** Tasks 1–5 plus GitHub PR/run evidence.

**Produces:** `WORKFLOW_CUTOVER_COMPLETE` only if state and external evidence reconcile.

- [ ] **Step 1: Write failing migration-simulation tests.** Cover uncommitted product work preservation, existing merged work import, existing PR recognition, stale evidence rejection, GitHub/Azure disagreement, two overlapping work items, external blocker without model escalation, fresh-clone resume, CI failure after local pass, duplicate QA defect, cancellation, supersession, incompatible state version, and no competing lifecycle owner.

- [ ] **Step 2: Run the migration simulation suite.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag migration`

Expected: FAIL because migration/cutover simulation and Sprint 4A mapping are incomplete.

- [ ] **Step 3: Import only trustworthy history.** Record PR #23/#24 merge references, E1 development run `33457927112`, existing test deployment history, known failed runs as historical failure evidence, and unverified gates as pending/stale. Do not claim test E1, D1, QA, or product completion.

- [ ] **Step 4: Implement cutover predicate.** `Set-WorkflowCutover` must require valid schemas, preserved worktree snapshot, selected plan/authority/current sprint/current work items, pre/post baselines, passing self-tests, no migration regression, and a reconciliation result with no unresolved contradiction. Otherwise write `CUTOVER_BLOCKED` and exact blockers only.

- [ ] **Step 5: Execute safe validation.**

Run: `pwsh -File ops/Test-SprintDelivery.ps1; pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

Expected: all deterministic workflow tests pass; no shared environment mutation; output either `WORKFLOW_CUTOVER_COMPLETE` or an evidenced blocking condition.

- [ ] **Step 6: Produce the required migration report.** Include repository snapshot, planning discovery, authority, Sprint 4A work-item reconstruction, KEEP/ADAPT/REPLACE decisions, imported/stale evidence, both baselines, model routing, hard enforcement, cutover result, human decisions, and exact next action.

- [ ] **Step 7: Commit.**

```powershell
git add delivery docs/evidence/delivery-workflow-migration.md docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md ops/tests/SprintDelivery.Tests.ps1
git commit -m "feat: cut over Sprint 4A delivery workflow"
```

### Task 7: Independent review, post-migration baseline, and handoff

**Files:**
- Modify: `delivery/evidence/cutover-validation.json`
- Modify: `docs/evidence/delivery-workflow-migration.md`
- Test: `ops/tests/SprintDelivery.Tests.ps1`

**Consumes:** completed Tasks 1–6.

**Produces:** independently reviewed workflow branch and exact next orchestrator action.

- [ ] **Step 1: Request independent review.** Review schema safety, state/evidence separation, branch/CI compatibility, mutation guards, imported Sprint 4A facts, secret handling, retry/escalation policy, and fresh-session usability. Classify findings `BLOCKER`, `HIGH`, `MEDIUM`, `LOW`, or `INFO` with evidence.

- [ ] **Step 2: Fix any BLOCKER/HIGH finding with a regression test first.**

Run: `Invoke-Pester ops/tests/SprintDelivery.Tests.ps1 -Tag <affected-tag>`

Expected: regression test fails before the focused correction and passes afterward.

- [ ] **Step 3: Run post-migration baseline.**

Run:

```powershell
dotnet restore CloudOrders.slnx
dotnet format CloudOrders.slnx --verify-no-changes --no-restore
dotnet build CloudOrders.slnx --configuration Release --no-restore
dotnet test CloudOrders.slnx --configuration Release --no-build --no-restore
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
pwsh -File ops/Test-SprintDelivery.ps1
git diff --check origin/development...HEAD
```

Expected: workflow-specific checks pass. If Testcontainers still cannot contact Docker, record the same `ENVIRONMENT_FAILURE` rather than retrying it until green.

- [ ] **Step 4: Update evidence and commit review closure.**

```powershell
git add delivery/evidence docs/evidence/delivery-workflow-migration.md
git commit -m "test: verify sprint delivery workflow cutover"
```

## Plan Self-Review

- **Coverage:** Tasks 1–3 implement versioned policy/state/evidence, derived completion, reconciliation, invalidation, and idempotency. Task 4 adds the focused skills/runbook. Task 5 provides safe CI enforcement. Tasks 6–7 import/reconcile Sprint 4A, execute the required self-tests/baselines, review, and cut over without product work.
- **Safety:** No task authorizes Azure, Entra, production, migration, branch deletion, force push, or environment mutation. The only Git changes are isolated workflow assets and evidence.
- **Evidence:** Every task has a failing test, command, expected result, and focused commit. Historical evidence remains distinct from post-migration evidence.
- **Consistency:** All commands use `ops/Invoke-SprintDelivery.ps1`, `ops/Test-SprintDelivery.ps1`, `delivery/config.json`, and `delivery/state.json`; all state transitions are schema-validated.
- **Scope:** The plan intentionally does not implement Sprint 4A product Tasks 4/5/D1. It resumes them only after cutover.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-05-sprint-delivery-workflow-migration.md`.

1. **Subagent-driven (recommended):** Dispatch a fresh agent per task and independently review each task.
2. **Inline execution:** Execute the tasks in this session with review checkpoints.
