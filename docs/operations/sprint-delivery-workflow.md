# Sprint delivery workflow

## Resume a fresh session

Start in the repository root. Read `delivery/config.json`, `delivery/state.json`, the selected sprint plan, and linked evidence. Then run:

```powershell
pwsh -File ops/Test-SprintDelivery.ps1
pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf
```

Do not infer completion from a label. Git is authoritative for commits and worktrees, GitHub for pull requests and checks, and cloud records for deployment and artifact facts. The orchestrator is the sole lifecycle writer; all role skills are read-only consumers of state.

## Evidence and staleness

Evidence must bind the command, outcome, immutable revision, run or artifact identifier when applicable, timestamp, and classification. Reconcile before any side effect. If an authoritative fact differs, mark dependent evidence `STALE`, retain the prior record, and request reconciliation rather than overwriting history. A changed revision, run, artifact, target, requirement, or state-schema version invalidates dependent gates until re-verified.

Classify an unavailable Docker daemon or equivalent unavailable local dependency as `ENVIRONMENT_FAILURE`, retain its output, and do not report it as a product defect. Restore the dependency and rerun the affected checks; do not consume retries without a distinct hypothesis.

## Normal delivery path

Use the selected task plan to keep planning, implementation, independent review, runtime validation, and QA separate. Implementation follows TDD and inspects affected persistent state directly. Record compact evidence before moving to another role. Retry commands only within the configured limit and with a new hypothesis. Repeated or unresolved failures require escalation with evidence, not improvisation.

## Cancellation and supersession

Never delete history to stop work. Record `CANCELLED` only with the reason, authority, and preserved evidence. Record `SUPERSEDED` with the successor requirement or work item and invalidate affected gates. A resumed item must reconcile again before action.

## Human decision boundaries

Stop and request an explicit decision before destructive data transition, production action, incompatible requirements, privileged identity or permission change, inaccessible credentials, external ownership decision, budget or domain choice, or any action not supported by evidence. The default command is read-only:

```powershell
pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf
```
