# Read-only SQL Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent GitHub OIDC from mutating Azure SQL identity/role state during development and test deployments.

**Architecture:** Retain the ownership precondition as the CI database gate and remove contained-user/role bootstrap SQL from its normal execution path. Database identity provisioning stays administrator-controlled.

**Tech Stack:** PowerShell, T-SQL, xUnit, Pester, GitHub Actions.

## Global Constraints

- Do not alter production deployment behavior.
- Do not grant new Azure SQL permissions to GitHub OIDC.
- Preserve a fail-closed, read-only ownership precondition before migration.

---

### Task 1: Separate administrator SQL provisioning from CI validation

**Files:**
- Create: `ops/Provision-CloudOrdersSqlIdentities.ps1`
- Modify: `ops/Bootstrap-CloudOrdersSql.ps1`
- Modify: `ops/Bootstrap-CloudOrdersSql.Tests.ps1`
- Create: `ops/Provision-CloudOrdersSqlIdentities.Tests.ps1`
- Modify: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`
- Modify: `docs/operations/azure-sql-bootstrap.md`
- Modify: `README.md`

- [ ] **Step 1: Write failing tests**

Assert the CI script's `-WhatIf` output contains the ownership query and contains neither `CREATE USER` nor `ALTER ROLE`. Mock `Invoke-Sqlcmd` and assert exactly one invocation. Assert the administrator script emits `CREATE USER` with expected identities and role grants, then validates each SID/application ID. Assert the workflow precondition invokes the CI script before the migration job.

- [ ] **Step 2: Run tests to verify red**

Run `Invoke-Pester -Path ops/Bootstrap-CloudOrdersSql.Tests.ps1` and the focused architecture test. Expect the new no-DDL/runtime-call assertions to fail.

- [ ] **Step 3: Implement minimal change**

Move identity/role DDL into `Provision-CloudOrdersSqlIdentities.ps1`, an explicit administrator-only command that takes expected API and migration application IDs, validates `sys.database_principals.sid`, and reports drift without changing CI permissions. Replace `$sql` in the CI script with the ownership query, remove the DDL construction and identity parameters, and make its `ShouldProcess` description read-only.

- [ ] **Step 4: Run verification**

Run both Pester suites, `dotnet test CloudOrders.slnx --configuration Release --no-restore`, `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`, and `git diff --check`. Run the administrator preflight read-only against development and test before deployment.

- [ ] **Step 5: Commit**

Commit with `fix: keep SQL bootstrap read-only in CI`.
