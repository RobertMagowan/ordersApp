# Simple Promotion Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the self-locking promotion controls with one ordinary pull-request check that enforces `feature/*` → `development` → `test` → `master`.

**Architecture:** GitHub branch protection remains the control that blocks direct, force-pushed, or deleted protected branches and requires CI. `.github/workflows/branch-policy.yml` becomes the single PR-time path validator; `.github/workflows/deploy.yml` deploys a protected branch after its merge and does not attempt to reconstruct PR ancestry.

**Tech Stack:** GitHub Actions YAML, .NET 10 xUnit architecture tests, Markdown contributor guidance.

## Global Constraints

- Promotion is only `feature/*` → `development` → `test` → `master` through pull requests.
- Protected branches use merge commits; squash and rebase merges remain disabled in GitHub.
- No direct push, force push, deletion, synthetic check run, `pull_request_target`, or post-merge ancestry validation is part of this policy.
- Production deployment remains separately user-authorised.

---

### Task 1: Define and implement the simple policy

**Files:**
- Modify: `.github/workflows/branch-policy.yml`
- Modify: `.github/workflows/deploy.yml`
- Modify: `tests/CloudOrders.ArchitectureTests/RepositoryPolicyTests.cs`
- Modify: `tests/CloudOrders.ArchitectureTests/DeploymentWorkflowPolicyTests.cs`
- Modify: `AGENTS.md`

**Interfaces:**
- Consumes: pull request base/head branch names supplied by `github.event.pull_request`.
- Produces: required GitHub Actions check named `Enforce promotion source branch`.

- [ ] **Step 1: Write failing architecture tests**

Replace the lineage assertions with checks for `pull_request:`, `Enforce promotion source branch`, `feature/*`, `development`, `test`, and `master`; assert absence of `pull_request_target:`, `push:`, `checks: write`, `gh api`, and `two-parent merge commit`. Replace the deployment lineage test with assertions that `deploy.yml` does not contain `Reject a protected push without merge lineage` or `Expected a two-parent merge commit`.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run: `dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter "FullyQualifiedName~RepositoryPolicyTests|FullyQualifiedName~DeploymentWorkflowPolicyTests"`

Expected: FAIL because the existing workflows use `pull_request_target`, synthetic checks, and merge-lineage checks.

- [ ] **Step 3: Replace the PR policy workflow**

Make `.github/workflows/branch-policy.yml` contain one `pull_request` trigger targeting `development`, `test`, and `master`; use read-only `contents: read` permission; validate `$BASE` and `$HEAD` in Bash with this exact mapping:

```bash
development) [[ "$HEAD" == feature/* ]] ;;
test) [[ "$HEAD" == development ]] ;;
master) [[ "$HEAD" == test ]] ;;
```

Fail unsupported branches and incorrect mappings with a clear error. Do not check out code, call the GitHub API, publish a synthetic status, or trigger on `push`.

- [ ] **Step 4: Remove duplicate deployment lineage enforcement**

In `deploy.yml`, remove the top-level `pull-requests: read` permission, the `Check out protected commit` step, and the complete `Reject a protected push without merge lineage` step. Retain branch-to-environment validation and the later E1/source checkouts required by deployment jobs.

- [ ] **Step 5: Update contributor guidance**

Replace the AGENTS promotion text with the PR-only sequence and explain that `branch-policy.yml` validates source/base pairs before merge. State that protected branches contain no independent product changes; a conflict that changes code is returned to `development`. Keep the one-developer review and merge-commit guidance.

- [ ] **Step 6: Run focused tests to verify they pass**

Run: `dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter "FullyQualifiedName~RepositoryPolicyTests|FullyQualifiedName~DeploymentWorkflowPolicyTests"`

Expected: PASS.

- [ ] **Step 7: Run repository verification**

Run `dotnet restore CloudOrders.slnx`, `dotnet format CloudOrders.slnx --verify-no-changes --no-restore`, `dotnet build CloudOrders.slnx --configuration Release --no-restore`, `dotnet test CloudOrders.slnx --configuration Release --no-build --no-restore`, and `git diff --check`.

Expected: every command exits with code 0.

- [ ] **Step 8: Commit the verified policy change**

Commit exactly the workflow, architecture-test, and AGENTS changes with subject `ci: simplify protected branch promotion`.
