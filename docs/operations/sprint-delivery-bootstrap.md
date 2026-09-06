# Sprint Delivery Bootstrap

For a fresh session, work from the repository root and read `AGENTS.md`, `delivery/config.json`, `delivery/state.json`, the sprint plan named by `sprintPlanSource`, and the linked evidence. Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ops/Test-SprintDelivery.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf
```

The command is read-only. Resolve a returned `HUMAN_DECISION_REQUIRED` or `HUMAN_REVIEW_REQUIRED` with a recorded decision or review; do not substitute a deployment, merge, or new lifecycle label. Git, GitHub, and Azure facts override stale delivery state.
