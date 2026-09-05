# Sprint Delivery Workflow Design

## Purpose

Replace conversational sprint orchestration with one repository-native, resumable delivery workflow. Existing GitHub Actions, Bicep, test runners, branch policy, and Azure environments remain deterministic subordinate tools; this design does not replace them.

## Discovery and authority

`docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md` is the roadmap. `docs/superpowers/plans/2026-08-27-sprint-4-external-id-authorization.md` is the current, more specific Sprint 4 plan. The versioned contract pack and later explicit user decisions supersede broader earlier planning text. The legacy `2026-08-16-sprints-2-4-execution-plan.md` is historical for Sprint 4.

The current sprint is **Sprint 4A — Identity and ownership**. The next implementation work is its owner-aware authorization and `/me` tasks. The E1 expansion is merged to `development` and its corrected development migration-only run succeeded; it has not yet been promoted to `test`.

## Keep, adapt, replace

| Classification | Mechanism | Treatment |
| --- | --- | --- |
| Keep | `AGENTS.md`, contracts, sprint plans, evidence folders | Retain as repository guidance and requirement/evidence sources. |
| Keep | GitHub PR policy, CI, Bicep validation, protected Azure deployments | Keep as hard delivery mechanisms; state reconciles to them. |
| Adapt | Existing plans and progress notes | Import evidence into a structured state record; mark contradictory or weak history `HISTORICAL_UNVERIFIED`. |
| Adapt | Agent testing/review expectations | Express them as explicit quality gates, evidence records, and task handoffs. |
| Replace | Conversation-only orchestration and ad-hoc progress ledgers | Replace with one orchestrator command and versioned state. |
| Remove later | Superseded Sprint 4 wording and stale status text | Do not delete during bootstrap; flag it and retire only through a reviewed documentation change. |

## Repository-native architecture

`delivery/` is the single workflow root:

- `delivery/config.json` is versioned policy: branch/environment mapping, canonical commands, risk rules, required gates, bounded retries, logical model roles, and human-decision boundaries.
- `delivery/state.json` is small, committed, schema-versioned current state. It stores sprint/work-item identifiers, lifecycle, orchestration stage, gate statuses, evidence references, known blockers, retries, and reconciliation fingerprints. It contains no secrets, raw logs, or tokens.
- `delivery/evidence/` stores compact, committed JSON records bound to commit, CI run, artifact, deployment, and command result. Existing `docs/evidence/` remains the human-readable evidence location and is referenced rather than copied.
- `delivery/schemas/` holds JSON Schemas for configuration, state, and evidence. A deterministic validator rejects incompatible versions and invalid transitions.
- `ops/Invoke-SprintDelivery.ps1` is the sole orchestrator entry point. It loads/validates state, reconciles Git/GitHub/Azure before side effects, selects the next permitted action, and records outcome. It does not implement builds, tests, deployments, or migrations itself.

Focused repository skills will be installed under `.agents/skills/`: `sprint-orchestrator`, `task-planning`, `task-implementation`, `automated-testing`, `failure-resolution`, `environment-validation`, and `qa`. Existing review guidance is reused rather than duplicated.

## State and gates

Lifecycle (`TODO`, `IN_PROGRESS`, `PR_OPEN`, `MERGED`, `DEV_DEPLOYED`, `QA_DEPLOYED`, `RELEASED`, `CANCELLED`, `SUPERSEDED`) is separate from orchestration stage and gate status. Completion is derived only when required current gates are `PASS` or `NOT_APPLICABLE`, evidence is current, and no blocking defect exists.

The orchestrator invalidates only affected evidence after code, test, requirement, schema, environment, or configuration changes. Git is authoritative for commits/branches; GitHub for PR/CI; Azure for deployment/artifact reality; workflow state records intent/history and never overrides those systems.

Each task follows: readiness and impact analysis → reviewed plan → TDD implementation → local validation/direct state inspection → independent review → PR/CI → development deployment and smoke → defect loop. Sprint completion adds regression, migration/contract/security/observability checks where applicable, independent sprint audit, then test-environment QA. Existing merged work is imported without pretending missing historical gates occurred.

## Safety, retries, and model routing

All operations are idempotent or state-checked first. The workflow blocks destructive schema/data operations, production actions, security/permission changes, contradictory requirements, and unavailable credentials behind `HUMAN_DECISION_REQUIRED`. It never bypasses CI or branch policies.

Retries are bounded and require a distinct hypothesis, code path, new evidence, or instrumentation. Failures are classified before product changes. A compact blocker dossier precedes escalation. Logical roles are `cheap_model`, `default_model`, and `strong_model`; this runtime may recommend escalation but must not claim it can switch models automatically. Environment/external blockers do not trigger speculative stronger-model retries.

## Cutover and Sprint 4A migration

Bootstrap runs in this isolated feature branch and changes only workflow assets. It first snapshots existing worktree/branch state and records imported evidence. It then validates state schemas, deterministic transition rules, reconciliation against GitHub, stale-evidence invalidation, duplicate-side-effect prevention, and a fresh-session resume simulation.

At cutover, Sprint 4A is imported as follows:

- Task 1, 2, 3, and 6: `MERGED` on `development`, with CI and local evidence imported where bound to their commits; missing historical gates remain explicit.
- E1: `DEV_DEPLOYED`, with successful run `33457927112` bound to merge commit `fbc68a9`; test promotion is pending.
- Task 4 and Task 5: `TODO`, sequential after E1 test promotion/state reconciliation.
- Task 7 D1/R1: blocked on its documented External ID and reset-or-mapped-backfill decision gates; it is not restarted or silently advanced.

Cutover requires a successful post-migration baseline, no workflow-caused regression, a validated state resume, and evidence that no competing workflow can advance the sprint. The next orchestrator action will reconcile the E1 development/test state and determine whether the existing approved E1-only promotion is the next safe action.

## Validation

The workflow implementation will add deterministic Pester tests for schema validation, transition legality, evidence invalidation, reconciliation conflicts, duplicate side-effect prevention, cancellation/supersession, stale evidence, overlapping work items, model-escalation boundaries, and fresh-session resume. It will run the repository's existing restore/format/build/unit/architecture/Bicep checks. Docker-backed integration results remain separately classified if Docker is unavailable.
