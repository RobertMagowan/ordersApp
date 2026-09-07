# Deployment Path Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent delivery-only changes from invoking Azure deployment while preserving existing deployable-change behavior.

**Architecture:** A PowerShell helper classifies changed files. The push/manual deployment workflow calls it before every Azure-mutating job. Pull requests remain outside `deploy.yml`.

**Tech Stack:** GitHub Actions, PowerShell 7, Pester 4, Git.

## Global Constraints

- Keep `deploy.yml` push/manual only; do not add a PR trigger.
- Do not change CI, branch policy, Azure resources, identity settings, or production approval.
- Treat a manual dispatch or indeterminate Git range as deployable.
- A skipped run must have no environment, OIDC permission, or Azure command.

---

### Task 1: Classify deployment scope

**Files:** Create `ops/Get-DeploymentScope.ps1`; modify `ops/tests/SprintDelivery.Tests.ps1`.

**Interface:** `Get-DeploymentScope -ChangedPaths <string[]> -ComparisonAvailable <bool>` returns `[pscustomobject]@{ deployable = <bool>; reason = <string> }`.

- [ ] **Step 1: Write the failing unit cases**

Add a `Describe 'Deployment path scope'` block that asserts:

```powershell
(Get-DeploymentScope -ChangedPaths @('docs/evidence/release.md','delivery/state.json','AGENTS.md') -ComparisonAvailable $true).reason | Should Be 'delivery_only'
(Get-DeploymentScope -ChangedPaths @('src/CloudOrders.Api/Program.cs') -ComparisonAvailable $true).reason | Should Be 'deployable_path'
(Get-DeploymentScope -ChangedPaths @('infra/main.bicep') -ComparisonAvailable $true).deployable | Should Be $true
(Get-DeploymentScope -ChangedPaths @('ops/releases/sprint-4a-e1-migration-only.json') -ComparisonAvailable $true).deployable | Should Be $true
(Get-DeploymentScope -ChangedPaths @('.github/workflows/deploy.yml') -ComparisonAvailable $true).deployable | Should Be $true
(Get-DeploymentScope -ChangedPaths @('docs/a.md','tests/CloudOrders.UnitTests/OrdersTests.cs') -ComparisonAvailable $true).deployable | Should Be $true
(Get-DeploymentScope -ChangedPaths @('docs/a.md') -ComparisonAvailable $false).reason | Should Be 'comparison_unavailable'
(Get-DeploymentScope -ChangedPaths @('unknown-root-file') -ComparisonAvailable $true).reason | Should Be 'unknown_path'
```

- [ ] **Step 2: Prove RED**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1`; it must fail because the helper does not exist.

- [ ] **Step 3: Implement the classifier**

Return `comparison_unavailable` immediately when comparison is unavailable. Mark `src/`, `tests/`, `infra/`, `local/`, `ops/releases/`, `ops/Get-DeploymentScope.ps1`, Docker and solution/build inputs, and `.github/workflows/deploy.yml` as `deployable_path`. Mark any unknown path as `unknown_path`; return `delivery_only` only when every path is known delivery-only. Provide direct-script standard-input JSON output for Actions.

- [ ] **Step 4: Prove GREEN and commit**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1`, then commit `feat: classify deployable change paths`.

### Task 2: Gate all Azure-mutating workflow jobs

**Files:** Modify `.github/workflows/deploy.yml` and `ops/tests/SprintDelivery.Tests.ps1`.

**Interface:** `classify_changes.outputs.deployable` and `.reason` are available to every deployment job.

- [ ] **Step 1: Write failing workflow-contract assertions**

Assert the workflow: has no `pull_request:` trigger; uses `fetch-depth: 0`; calls `ops/Get-DeploymentScope.ps1`; contains `comparison_unavailable`; and gates every Azure-mutating job on `needs.classify_changes.outputs.deployable == 'true'`. Cover `preview_foundation`, `prepare_release`, `preview_sql`, `bootstrap_sql`, `run_migration`, `run_sprint_4a_e1_migration_only`, and `deploy_release`. Assert the no-deployment summary has no `environment:` or `id-token: write`.

- [ ] **Step 2: Prove RED**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1`; it must fail because `classify_changes` does not exist.

- [ ] **Step 3: Implement classifier workflow jobs**

Add a read-only `classify_changes` job after `validate_promotion_ref`, with `contents: read`, no environment, and pinned checkout at `fetch-depth: 0`. For `workflow_dispatch`, output `deployable=true` and `reason=manual_dispatch`. For pushes, validate the non-zero 40-character `github.event.before`, fetch it if missing, and diff it against `github.sha`. Pass the file list to the PowerShell helper. If validation, fetching, or revision lookup fails, invoke the helper with comparison unavailable.

Add a no-environment `deployment_not_required` summary job for `deployable == 'false'`. It must disclose only range and reason.

- [ ] **Step 4: Gate existing jobs**

Add `classify_changes` to the `needs` and preserve existing conditions while adding `deployable == 'true'` to every listed job, including E1-only migration. Leave `validate_promotion_ref` unchanged.

- [ ] **Step 5: Prove GREEN and commit**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1` and `gh workflow view deploy.yml --repo RobertMagowan/ordersApp`. Commit `fix: skip deployment for delivery-only changes`.

### Task 3: Verify boundaries and prepare PR

**Files:** Update the design only if validation uncovers a real design mismatch.

- [ ] **Step 1: Run full local verification**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1`, `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`, `dotnet build CloudOrders.slnx --configuration Release --no-restore`, and `git diff --check origin/development...HEAD`. All commands must exit 0 and the build must have zero warnings/errors.

- [ ] **Step 2: Check change boundaries**

Use `git merge-base HEAD origin/development` and inspect its changed paths. Only the classifier, deploy workflow, focused tests, approved design, and plan may change; Azure parameters, identity settings, and production resource files must remain untouched.

- [ ] **Step 3: Push and create draft PR**

Push `feature/deployment-path-filter` and create a draft PR to `development` titled `fix: skip deployment for delivery-only changes`. CI and delivery validation should run without Azure deployment.

## Plan self-review

- Task 1 implements the fail-closed policy and every approved classification case.
- Task 2 covers push/manual comparison, E1-only migration, all Azure-mutating jobs, and no-environment skipping.
- Task 3 verifies the approved no-other-workflow boundary and uses the normal PR handoff.
