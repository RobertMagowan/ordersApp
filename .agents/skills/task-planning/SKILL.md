---
name: task-planning
description: Produce small, testable sprint task plans from authoritative requirements.
---

# Task planning

Turn the selected work item into a small, independently reviewable implementation plan.

## Inputs

- Read-only delivery state, authoritative requirements, and reconciled evidence
- The orchestration command: `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

## Outputs

- Ordered, testable task slices with acceptance criteria, risks, and decision boundaries
- A compact handoff for implementation and independent review

## Procedure

Read state without changing it; you must not advance lifecycle state. Separate requirement discovery from implementation and review. Identify direct state verification appropriate to each slice and specify the failing test before code. Surface incompatible requirements, privileged changes, or destructive actions as human decisions. Do not take destructive or production effects without explicit authority.
