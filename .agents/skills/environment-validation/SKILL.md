---
name: environment-validation
description: Validate approved releases from authoritative runtime evidence.
---

# Environment validation

Validate a released task against authoritative runtime evidence after an approved promotion.

## Inputs

- Read-only release identity, deployment evidence, smoke criteria, and current state
- The orchestration command: `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

## Outputs

- Smoke result, direct runtime or data-state observations, and evidence links
- A concise remediation or QA handoff

## Procedure

Read state without changing it; you must not advance lifecycle state. Reconcile exact revision, run, artifact, and target identifiers before testing. Perform technology-appropriate smoke checks and inspect affected state directly where allowed. Classify failures from evidence and request a human decision for access, privileged, destructive, or production actions. Do not take destructive or production effects without explicit authority.
