# Migration Job Ownership Precondition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the Sprint 4B ownership precondition with the existing migration managed identity, not GitHub OIDC.

**Architecture:** The migration executable gains a deliberately narrow `--ownership-precondition` mode. The deployment workflow executes and verifies that mode in an immutable Container Apps Job before starting the named EF migration. It retains fail-closed behavior and does not grant SQL permissions to GitHub Actions.

**Tech Stack:** .NET 10/C# 14, EF Core 10, Azure Container Apps Jobs, GitHub Actions, xUnit.

## Global Constraints

- Development and test only; production remains excluded.
- The precondition is read-only and must precede `EnforceCustomerProfileOwnership`.
- Use the existing migration identity; do not grant GitHub OIDC a database principal or role.

### Task 1: Define the executable contract

**Files:**
- Modify: `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`
- Modify: `src/CloudOrders.Migrations/Program.cs`

- [ ] Add a failing integration test that starts the migration executable with `--ownership-precondition` against an E1-shaped database containing an unsafe ownership row and expects a non-zero exit code and the safe precondition failure category.
- [ ] Add a failing integration test that runs the same command against valid data and expects success.
- [ ] Run the focused tests and confirm the option is rejected before implementation.
- [ ] Implement the option using a transaction containing the four count checks and a rollback; return a dedicated safe failure category on violation.
- [ ] Re-run the focused migration-runner tests and commit `feat: run ownership precondition in migration job`.

### Task 2: Invoke the contract from the release workflow

**Files:**
- Modify: `.github/workflows/deploy.yml`
- Modify: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`

- [ ] Add a failing architecture test that requires a precondition Job execution with exactly `--ownership-precondition`, verifies the execution arguments, and requires it before the named migration execution.
- [ ] Run the architecture test and confirm it fails because the workflow still calls `Bootstrap-CloudOrdersSql.ps1` from GitHub OIDC.
- [ ] Replace that CI PowerShell call with the immutable-job precondition execution and bounded status polling; retain the existing named migration execution and its argument validation.
- [ ] Re-run the architecture and workflow contract tests; commit `fix: run SQL precondition through migration identity`.

### Task 3: Verify and promote

- [ ] Run format, Release build, full tests, Bicep build and parameter builds, and `git diff --check`.
- [ ] Create a `feature/*` pull request, address review feedback, and rely on the configured auto-merge to development.
- [ ] Verify development deployment, promote to test, and verify test migration and smoke test. Do not deploy production.
