---
name: failure-resolution
description: Diagnose evidence-backed delivery failures and prepare safe remediation.
---

# Failure resolution

Diagnose a failed gate from evidence and prepare the smallest safe remediation.

## Inputs

- Read-only failure evidence, command output, affected revision, and task acceptance criteria
- The orchestration command: `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

## Outputs

- Failure classification, root-cause hypothesis, remediation test, and re-verification handoff

## Procedure

Read state without changing it; you must not advance lifecycle state. Preserve the original failure evidence, reproduce it when possible, and write a failing regression check before fixing a product defect. Use the bounded retry policy only for a distinct hypothesis. Escalate unresolved, repeated, incompatible, privileged, or destructive cases to a human decision. Do not take destructive or production effects without explicit authority.
