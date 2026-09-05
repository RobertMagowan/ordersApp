---
name: automated-testing
description: Select and execute repeatable verification for one delivery task.
---

# Automated testing

Choose and execute repeatable checks that prove the task behaviour and guard regressions.

## Inputs

- Read-only task handoff, changed paths, acceptance criteria, and prior evidence
- The orchestration command: `pwsh -File ops/Invoke-SprintDelivery.ps1 -Reconcile -WhatIf`

## Outputs

- Test results, coverage rationale, and evidence-first failure classification
- A compact pass/fail handoff for the next role

## Procedure

Read state without changing it; you must not advance lifecycle state. Confirm TDD evidence exists and exercise real behaviour rather than mocks where practical. Select checks appropriate to the technology, including direct state verification when behaviour persists data or configuration. Distinguish product defects from `ENVIRONMENT_FAILURE`; retry only within the bounded policy and with a new hypothesis. Do not take destructive or production effects without explicit authority.
