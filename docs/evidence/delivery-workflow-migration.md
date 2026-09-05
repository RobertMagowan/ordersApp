# Sprint Delivery Workflow Migration

**Cutover result:** `WORKFLOW_CUTOVER_COMPLETE`. The repository workflow may resume Sprint 4A at work item `4A-4`; no external system was changed during cutover.

## Repository and authority snapshot

- Workflow branch: `feature/sprint-delivery-workflow`, based on development commit `fbc68a9f0e02923880c8a06162a8d7cda2afac38`.
- Current authority: the specific Sprint 4 plan at `docs/superpowers/plans/2026-08-27-sprint-4-external-id-authorization.md`, its versioned contracts, and later approved decisions. The broader roadmap remains the delivery roadmap.
- The workflow has one lifecycle owner: `sprint-orchestrator`. Git, GitHub, and Azure remain authoritative for repository, PR/CI, and deployment facts respectively.

## Imported Sprint 4A facts

Read-only GitHub reconciliation confirmed PR #23 merged at `0bf4012d16a74361500dad806d120a556fbbfd9b` and PR #24 at `fbc68a9f0e02923880c8a06162a8d7cda2afac38`. Development run `33457927112` succeeded for the latter commit. Earlier test deployment run `33066467680` succeeded for `bbfcbe4f792790703a877a02f2941cfada61d6ae`; it is historical and does not contain E1. Failed development run `33381147912` is retained as historical failure evidence, not as a retry instruction. These facts are imported in [cutover-validation.json](../../delivery/evidence/cutover-validation.json); they do not prove E1 test deployment, QA, D1, or product completion.

Tasks 1, 2, 3, and 6 stay historical merged work. E1 is historical `DEV_DEPLOYED`; Tasks 4 and 5 remain active/pending. D1 retains its `HUMAN_DECISION_REQUIRED` External ID/data-transition blocker. The preserved product worktree is `feature/sprint4-identity-design` at `5c0e1ab136f477127ce194426742a27d704d20d4`.

## Evidence and decisions

The pre-migration baseline is recorded in [pre-migration-baseline.json](../../delivery/evidence/pre-migration-baseline.json): restore, format, Release build, architecture, unit, and Bicep checks passed; Docker-backed integration failures remain `ENVIRONMENT_FAILURE`. Historical failed runs are retained as historical failure evidence and do not become a retry or completion claim.

The post-migration baseline targeted `4b9b946ca12562900684fe5d2d7106d7f38d08e7`. Restore, format verification, Release build (zero warnings and errors), Bicep lint/build, and all three parameter builds passed. Architecture tests passed 24/24, unit tests 13/13, and Docker-backed integration tests 73/73. The delivery suite passed 54/54 through the repository-supported Windows PowerShell 5.1 path. The plan's literal `pwsh` command could not start because PowerShell 7 is not installed locally; this is retained as an environment/tooling failure, not a product or workflow-test failure.

Read-only GitHub and Azure checks then bound run `33457927112` at `fbc68a9f0e02923880c8a06162a8d7cda2afac38` to ACR artifact `cloudorders-migrations@sha256:dbce8f8c093c7781796dd35e785dc7703b906e394aef7d76920632f20a6fd87e` and succeeded job execution `cloudorders-dev-migrations-mefmiwa` for `AddCustomerProfileOwnershipExpand`. No deployment, migration, repository, or environment mutation was performed.

Keep protected promotions, CI, Bicep, and Azure workflows. Adapt plans and historic evidence into the versioned delivery state. Replace the conversational ledger as the lifecycle authority; it remains only ignored scratch context.

## Independent review and closure

Independent review initially found one blocker and three high-severity defects: blocked cutover could resume product work, deployment evidence lacked artifact identity, released work could have no evidence, and the documented CLI could not consume sufficient cutover proof. Regression-first corrections in `6be5c69861c6ff00ee31541f57d692e5a884e05c` and `4b9b946ca12562900684fe5d2d7106d7f38d08e7` now fail closed and require matching immutable deployment evidence plus committed baseline/self-test proof. Two medium follow-ups remain non-blocking: adopting a full draft-2020-12 runtime schema engine and recording richer invalidation provenance across every dependent gate.

## Next action

Resume Sprint 4A at `4A-4` (owner-aware authorization), followed by `4A-5`. The later `4A-7-D1` item retains its existing `HUMAN_DECISION_REQUIRED` External ID/data-transition decision and must not be advanced implicitly.
