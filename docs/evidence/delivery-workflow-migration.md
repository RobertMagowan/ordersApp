# Sprint Delivery Workflow Migration

**Cutover result:** `CUTOVER_BLOCKED` — no lifecycle is advanced and no external system was changed.

## Repository and authority snapshot

- Workflow branch: `feature/sprint-delivery-workflow`, based on development commit `fbc68a9f0e02923880c8a06162a8d7cda2afac38`.
- Current authority: the specific Sprint 4 plan at `docs/superpowers/plans/2026-08-27-sprint-4-external-id-authorization.md`, its versioned contracts, and later approved decisions. The broader roadmap remains the delivery roadmap.
- The workflow has one lifecycle owner: `sprint-orchestrator`. Git, GitHub, and Azure remain authoritative for repository, PR/CI, and deployment facts respectively.

## Imported Sprint 4A facts

Read-only GitHub reconciliation confirmed PR #23 merged at `0bf4012d16a74361500dad806d120a556fbbfd9b` and PR #24 at `fbc68a9f0e02923880c8a06162a8d7cda2afac38`. Development run `33457927112` succeeded for the latter commit. These facts are imported in [cutover-validation.json](../../delivery/evidence/cutover-validation.json); they do not prove test deployment, QA, D1, or product completion.

Tasks 1, 2, 3, and 6 stay historical merged work. E1 is historical `DEV_DEPLOYED`; Tasks 4 and 5 remain active/pending. D1 retains its `HUMAN_DECISION_REQUIRED` External ID/data-transition blocker. The preserved product worktree is `feature/sprint4-identity-design` at `5c0e1ab136f477127ce194426742a27d704d20d4`.

## Evidence and decisions

The pre-migration baseline is recorded in [pre-migration-baseline.json](../../delivery/evidence/pre-migration-baseline.json): restore, format, Release build, architecture, unit, and Bicep checks passed; Docker-backed integration failures remain `ENVIRONMENT_FAILURE`. Historical failed runs are retained as historical failure evidence and do not become a retry or completion claim.

Keep protected promotions, CI, Bicep, and Azure workflows. Adapt plans and historic evidence into the versioned delivery state. Replace the conversational ledger as the lifecycle authority; it remains only ignored scratch context.

## Why cutover is blocked

Azure deployment/artifact facts could not be obtained through the available read-only reconciliation, and the post-migration baseline belongs to Task 7. Both conditions are explicit in `delivery/state.json`; no missing proof is invented. The deterministic simulation proves a complete cutover only when schemas, the preserved-worktree snapshot, both baselines, passing self-tests, one lifecycle owner, and an agreeing reconciliation result are supplied.

## Next action

Complete Task 7's independent review and post-migration baseline. Then run `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf` with a read-only Azure evidence provider. Only an agreeing result may change the cutover status; after that, resume Sprint 4A from the recorded Task 4/Task 5 sequence while leaving D1 blocked pending a human decision.
