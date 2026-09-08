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

### Task 1: Make CI SQL bootstrap read-only

**Files:**
- Modify: `ops/Bootstrap-CloudOrdersSql.ps1`
- Modify: `ops/Bootstrap-CloudOrdersSql.Tests.ps1`
- Modify: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`

- [ ] **Step 1: Write failing tests**

Assert the PowerShell `-WhatIf` output contains the ownership query and contains neither `CREATE USER` nor `ALTER ROLE`; assert the workflow precondition invokes the same script before the migration job.

- [ ] **Step 2: Run tests to verify red**

Run `Invoke-Pester -Path ops/Bootstrap-CloudOrdersSql.Tests.ps1` and the focused architecture test. Expect the new no-DDL assertions to fail.

- [ ] **Step 3: Implement minimal change**

Remove `$bootstrapSql` construction and the second `Invoke-Sqlcmd` call. Keep the existing production guard, token acquisition, identifier validation, and read-only transaction call.

- [ ] **Step 4: Run verification**

Run Pester, `dotnet test CloudOrders.slnx --configuration Release --no-restore`, `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`, and `git diff --check`.

- [ ] **Step 5: Commit**

Commit with `fix: keep SQL bootstrap read-only in CI`.
