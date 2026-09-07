# Nonproduction Promotion Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Automatically merge eligible nonproduction pull requests after checks
and conversation resolution, then automatically deploy development and test.

**Architecture:** GitHub branch protection remains the authority for checks and
conversation resolution. A narrowly-scoped repository workflow requests
GitHub auto-merge for eligible same-repository pull requests. GitHub
environment configuration removes reviewer pauses only for development and
test; the existing deployment workflow remains the sole Azure mutator.

**Tech Stack:** GitHub Actions YAML, GitHub CLI/API, xUnit architecture tests.

## Global Constraints

- Permit only feature/* to development and development to test.
- Never auto-merge test to master or change existing production deployment.
- Preserve merge commits, direct-push prohibition, checks, and conversation
  resolution.
- A failed Azure deployment is diagnosed and fixed; it is never blindly
  retried.

---

### Task 1: Guarded auto-merge workflow

**Files:**
- Create: .github/workflows/auto-merge-nonproduction.yml
- Modify: tests/CloudOrders.ArchitectureTests/RepositoryPolicyTests.cs

**Interfaces:**
- Consumes: GitHub pull_request event fields for draft state, head repository,
  source branch, and base branch.
- Produces: a GitHub auto-merge request using merge commits for only an
  eligible nonproduction pull request.

- [ ] **Step 1: Write the failing architecture test**

Add a fact that requires a workflow with pull_request triggers, only
contents: write and pull-requests: write permissions, same-repository
validation, feature/* to development, development to test, and a merge-mode
auto-merge command. Assert that the workflow contains no master or production
target and no review approval, dismissal, or conversation-resolution command.

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

~~~powershell
dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter "FullyQualifiedName~AutoMerge"
~~~

Expected: failure because the auto-merge workflow does not yet exist.

- [ ] **Step 3: Create the minimal workflow**

Create a pull_request workflow with:

~~~yaml
permissions:
  contents: write
  pull-requests: write
~~~

Guard its only job with non-draft status, same-repository head, and the two
authorised base/source combinations. Invoke:

~~~bash
gh pr merge "$PR_URL" --auto --merge
~~~

Do not use pull_request_target, checkout pull-request code, a PAT, or
production/base-master conditions.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the Step 2 command. Expected: one passing auto-merge architecture test.

- [ ] **Step 5: Commit the task**

~~~powershell
git add .github/workflows/auto-merge-nonproduction.yml tests/CloudOrders.ArchitectureTests/RepositoryPolicyTests.cs
git commit -m "ci: automate nonproduction pull request merges"
~~~

### Task 2: Document automated nonproduction deployment

**Files:**
- Modify: AGENTS.md
- Modify: docs/operations/sprint-delivery-workflow.md
- Test: tests/CloudOrders.ArchitectureTests/RepositoryPolicyTests.cs

**Interfaces:**
- Consumes: the auto-merge workflow and GitHub environment configuration.
- Produces: a precise operator rule: review conversations block auto-merge,
development/test deploy after merge, and test-to-master and production remain
excluded from auto-merge.

- [ ] **Step 1: Write the failing documentation-policy test**

Add assertions requiring the repository guide and sprint runbook to say that
development/test merge and deploy automatically after required checks and
resolved conversations, that review feedback is assessed and addressed before
resolution, and that test-to-master and production remain excluded from
auto-merge.

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

~~~powershell
dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter "FullyQualifiedName~NonproductionPromotion"
~~~

Expected: failure because the documented policy is not yet present.

- [ ] **Step 3: Add the minimal documentation**

Update the guide and runbook with the automatic nonproduction merge/deploy
rule. State that unresolved review conversations prevent auto-merge and that
deployment failure requires diagnosis, a new feature branch, validation, and
normal promotion rather than a blind retry.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the Step 2 command. Expected: passing documentation-policy test.

- [ ] **Step 5: Commit the task**

~~~powershell
git add AGENTS.md docs/operations/sprint-delivery-workflow.md tests/CloudOrders.ArchitectureTests/RepositoryPolicyTests.cs
git commit -m "docs: clarify nonproduction automation"
~~~

### Task 3: Apply and verify GitHub settings

**Files:**
- No repository file changes.

**Interfaces:**
- Consumes: repository administrator GitHub CLI session.
- Produces: repository auto-merge enabled; development/test environments have
  no required-reviewer rule; production configuration is unchanged.

- [ ] **Step 1: Capture the existing settings**

Run:

~~~powershell
gh repo view RobertMagowan/ordersApp --json mergeCommitAllowed,defaultBranchRef
gh api repos/RobertMagowan/ordersApp/environments
~~~

Expected: merge commits are allowed; development/test contain a
required_reviewers rule; production is recorded for comparison.

- [ ] **Step 2: Enable repository auto-merge**

Run:

~~~powershell
gh api --method PATCH repos/RobertMagowan/ordersApp --raw-field allow_auto_merge=true
~~~

- [ ] **Step 3: Remove only nonproduction reviewer gates**

Delete the required-reviewer protection rule for development and test using
the rule identifiers captured in Step 1. Do not alter branch-policy rules or
the production environment.

- [ ] **Step 4: Verify the settings**

Run:

~~~powershell
gh api repos/RobertMagowan/ordersApp/environments
gh api repos/RobertMagowan/ordersApp/branches/development/protection
gh api repos/RobertMagowan/ordersApp/branches/test/protection
gh api repos/RobertMagowan/ordersApp/branches/master/protection
~~~

Expected: development/test have no required_reviewers rule; all three branches
still require pull requests, current checks, and conversation resolution; the
production response is unchanged.

### Task 4: Full validation and pull request

**Files:**
- Verify all files from Tasks 1 and 2.

- [ ] **Step 1: Run repository validation**

~~~powershell
dotnet format CloudOrders.slnx --verify-no-changes
dotnet build CloudOrders.slnx --configuration Release
dotnet test CloudOrders.slnx --configuration Release
az bicep build --file infra/main.bicep
az bicep lint --file infra/main.bicep
git diff --check
~~~

Expected: format, build, tests, Bicep validation, and whitespace checks pass.

- [ ] **Step 2: Push and create the feature pull request**

~~~powershell
git push -u origin feature/automate-nonproduction-promotion
gh pr create --base development --head feature/automate-nonproduction-promotion --title "ci: automate nonproduction promotion"
~~~

Expected: CI passes; the new workflow enables auto-merge after its own PR is
reviewed and conversations are resolved.
