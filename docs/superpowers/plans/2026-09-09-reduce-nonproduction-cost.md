# Non-production Cost Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce development and test idle cost while retaining automatic deployment and on-demand health verification.

**Architecture:** Bicep environment parameter overlays own the non-production replica count, while the shared Azure SQL module owns the non-production serverless pause period. The deployment workflow remains the source of operational retries, which are tested by static workflow assertions and live smoke tests.

**Tech Stack:** Azure Bicep, Azure Container Apps Consumption, Azure SQL Database Serverless, GitHub Actions, Pester.

## Global Constraints

- Preserve separate development and test resource groups and SQL logical servers.
- Do not use the Azure SQL free offer or alter production behaviour.
- Preserve the `feature/*` → `development` → `test` → `master` promotion model.
- Do not add secrets or customer data to source control.

---

### Task 1: Declare low-idle infrastructure defaults

**Files:**
- Modify: `infra/environments/development.bicepparam`
- Modify: `infra/environments/test.bicepparam`
- Modify: `infra/environments/production.bicepparam`
- Modify: `infra/modules/sql-database.bicep:16`
- Test: `ops/tests/NonProductionCostControls.Tests.ps1`

**Interfaces:**
- Consumes: Bicep `minReplicas` parameter and AVM `autoPauseDelay` input.
- Produces: Scale-to-zero non-production Container Apps, an unchanged production replica floor, and 15-minute SQL auto-pause for non-production deployments.

- [x] **Step 1: Write the failing configuration test**

```powershell
It 'uses scale-to-zero and a 15 minute serverless SQL pause period' {
    $development = Get-Content "$PSScriptRoot/../../infra/environments/development.bicepparam" -Raw
    $test = Get-Content "$PSScriptRoot/../../infra/environments/test.bicepparam" -Raw
    $production = Get-Content "$PSScriptRoot/../../infra/environments/production.bicepparam" -Raw
    $sql = Get-Content "$PSScriptRoot/../../infra/modules/sql-database.bicep" -Raw

    $development | Should -Match 'param minReplicas = 0'
    $test | Should -Match 'param minReplicas = 0'
    $production | Should -Match 'param minReplicas = 1'
    $sql | Should -Match 'autoPauseDelay: 15'
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `Invoke-Pester ./ops/tests/NonProductionCostControls.Tests.ps1 -Output Detailed`

Expected: failure because non-production has no scale-to-zero override and SQL pauses after 60 minutes.

- [x] **Step 3: Make the minimal Bicep change**

```bicep
param minReplicas = 0
```

```bicep
autoPauseDelay: 15
```

- [x] **Step 4: Run the test and Bicep validation**

Run: `Invoke-Pester ./ops/tests/NonProductionCostControls.Tests.ps1 -PassThru; az bicep lint --file infra/main.bicep; az bicep build --file infra/main.bicep; az bicep build-params --file infra/environments/development.bicepparam; az bicep build-params --file infra/environments/test.bicepparam; az bicep build-params --file infra/environments/production.bicepparam`

Expected: tests pass and every command exits zero.

- [x] **Step 5: Commit**

```powershell
git add infra/main.bicep infra/modules/sql-database.bicep ops/tests/NonProductionCostControls.Tests.ps1
git commit -m "feat: scale nonproduction services to zero"
```

### Task 2: Verify workflow cold-start tolerance

**Files:**
- Create: `ops/tests/NonProductionCostControls.Tests.ps1`
- Inspect: `.github/workflows/deploy.yml:997-1090`
- Test: `ops/tests/NonProductionCostControls.Tests.ps1`

**Interfaces:**
- Consumes: deployed API `/health/live` and `/health/ready` endpoints.
- Produces: regression protection that the pipeline keeps bounded candidate and health retries needed by zero-replica apps.

- [x] **Step 1: Extend the workflow characterization test**

```powershell
It 'keeps bounded candidate and health retries for on-demand startup' {
    $workflow = Get-Content "$PSScriptRoot/../../.github/workflows/deploy.yml" -Raw

    $workflow | Should -Match 'Wait for candidate revision'
    $workflow | Should -Match 'for attempt in \{1\.\.30\}'
    $workflow | Should -Match '/health/live'
}
```

- [x] **Step 2: Run the test to verify it fails only if retry coverage is missing**

Run: `Invoke-Pester ./ops/tests/NonProductionCostControls.Tests.ps1 -PassThru`

Expected: pass for the existing workflow; this is a regression characterization, so no workflow modification is required.

- [ ] **Step 3: Run deployment-stage verification after promotion**

Run: trigger the automatic development deployment through the approved PR path, then confirm the workflow’s candidate and `/health/live` smoke-test steps succeed.

Expected: a zero-replica API starts on demand and the deployment succeeds without manual enablement.

- [ ] **Step 4: Commit evidence only when required by the sprint workflow**

```powershell
git add docs/evidence/sprint-4
git commit -m "docs: record nonproduction cost control verification"
```

## Self-review

- Coverage: scale-to-zero, test on-demand operation, SQL auto-pause, workflow retry tolerance, and no production/free-tier/shared-server change are included.
- Placeholder scan: none.
- Consistency: the test path and Bicep paths match the repository structure.
