---
name: task-implementation
description: Implement one approved task slice with test-first verification.
---

# Task implementation

Implement one approved task slice with a reproducible local verification record.

## Inputs

- An approved task handoff, read-only state, acceptance criteria, and relevant evidence
- The orchestration command: `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

## Outputs

- Focused change, failing-then-passing automated checks, and direct state-verification evidence
- A concise handoff for independent review

## Procedure

Read state without changing it; you must not advance lifecycle state. Follow TDD: capture the expected failing check, make the smallest change, then run focused and relevant regression checks. Inspect affected persistent state directly where applicable. Record commands, outcomes, and failure classification before retrying. Do not take destructive or production effects without explicit authority.
