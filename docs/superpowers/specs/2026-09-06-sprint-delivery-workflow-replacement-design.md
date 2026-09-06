# Sprint Delivery Workflow Replacement Design

## Purpose

Replace the repository's version-1 sprint orchestration with the approved bootstrap workflow. This is an in-place migration: it preserves product history, branches, worktrees, CI/CD, Azure environments, and the PR promotion path. It does not restart Sprint 4A or deploy production.

## Authority and boundaries

The approved bootstrap prompt is the workflow requirements authority. The Sprint 4 plan and its later approved amendments remain product-requirements authority. Git is authoritative for commits/worktrees, GitHub for PRs and checks, and Azure for deployed artifacts and migrations. The new orchestrator is the sole writer of lifecycle state.

During migration, product progression is frozen. Existing automation is retained only as subordinate deterministic tooling; no script may autonomously advance lifecycle state. Protected promotion remains `feature/*` → `development` → `test` → `master` through merge-commit PRs and environment approvals.

## Replacement architecture

Versioned configuration remains in `delivery/`, while runtime state is replaced by a schema that keeps these dimensions separate: work-item lifecycle, PR lifecycle, review status, orchestration stage, risk, gates, acceptance criteria, evidence, retries, blockers, and immutable artifacts. State records requirement/sprint provenance and SHA-bound review and CI evidence. A reconciliation command reads Git, GitHub, and Azure snapshots before any side effect, marks contradicted evidence stale, and fails closed on unresolved state.

Focused role instructions support planning, implementation, independent review, development validation, and QA. They consume state and evidence but do not own transitions. Deterministic validation remains in PowerShell, GitHub Actions, .NET tests, Bicep, and Azure tooling.

## Migration and cutover

The migration will: capture baseline repository/PR/CI/deployment facts; classify existing mechanisms as keep/adapt/replace/remove-later; reconstruct Sprint 4A and all active work; import only SHA-bound, authoritative evidence; add workflow self-tests; run a post-migration baseline; then record `WORKFLOW_CUTOVER_COMPLETE` only if reconciliation agrees. Existing open PRs #31 and #33 remain preserved and are not progressed during the freeze.

Current evidence establishes that the E1 migration is deployed and smoke-tested in development and test, while the delivery state must be reconciled because it still references obsolete commits/runs. No claim is made about unfinished 4A work items or D1's existing human decision gate.

## Failure handling and verification

Every retry needs a distinct hypothesis and stays within a configured budget. Requirements conflicts, destructive data work, privileged permission changes, and production actions require a human decision. Inconclusive reconciliation, incompatible state migration, or exhausted diagnosis requires human technical review. The replacement is validated through non-destructive state-transition fixtures and the repository baseline; it never tests itself by mutating production.
