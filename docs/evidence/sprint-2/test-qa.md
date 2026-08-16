# Sprint 2 Azure test QA

Status: **BLOCKED — not run**

Recorded: 2026-08-16T19:01:17Z

## Blocking condition

The Task 2 candidate is only on `feature/replan-sprint-assurance`. Repository policy requires a reviewed `feature/*` → `development` PR and successful development deployment, followed by a reviewed `development` → `test` PR. This task was explicitly instructed not to create a PR or bypass branch policy. Consequently `ordersapp-test` remains empty and there is no candidate endpoint, revision, image digest, or deployment run for QA.

## Required QA-only matrix after test promotion

An agent that does not edit implementation must record the test deployment run, Git SHA, Bicep deployment name, image digest, revision, endpoint, commands, outcomes, defects, and re-tests for:

- successful: `/health/live`, `/health/ready`, create an order, then retrieve it;
- boundary: minimum and maximum currently supported quantity and representative reference lengths;
- invalid: zero/negative quantity, empty required fields, unknown JSON members, malformed and unknown order IDs;
- failure/recovery: a bounded invalid request burst followed by healthy probes and a valid create/read flow;
- state integrity: created order fields/status remain unchanged across repeated reads;
- concurrency: concurrent independent creates remain individually retrievable with distinct IDs;
- regression: HTTPS/TLS validation, 404 behavior, content types, readiness, and the deployed image being a digest reference;
- platform integrity: Container App is running on the summary revision, release tag equals the summary Git SHA, ACR admin is disabled, and the rollback image remains resolvable when one exists.

Any defect blocks onward promotion and must be corrected on a fresh `feature/*` branch before repeating the affected development and test gates.
