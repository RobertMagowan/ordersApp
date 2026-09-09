# Task 1 review: Define the E2 schema contract

## Findings

No correctness or scope defects found.

- The committed diff adds tests in the three files named by the brief. The only additional file is the requested task evidence report; there are no production mappings, migrations, model snapshots, or other implementation changes.
- The integration compatibility test exercises a raw D1-shaped write with `SubjectId = NULL`, while supplying `Orders.CustomerProfileId`, `ActorCustomerProfileId`, and `TargetCustomerProfileId`. This correctly remains red against the pre-E2 schema and will prove that the retained legacy subject is nullable after E2.
- The migration-runner test verifies the actual SQL Server schema rather than only source text: all three ownership columns are `NOT NULL`, `SubjectId` is nullable, and the primary-key columns are ordered `(ActorCustomerProfileId, IdempotencyKey)`.
- The architecture test requires the named E2 migration and checks that ownership alterations, key replacement, nullable `SubjectId`, and absence of a `SubjectId` drop are represented in migration source. These checks complement the runtime schema assertions.
- The report's noted red test outcomes are consistent with a tests-first task where the E2 migration is intentionally absent. The focused integration command also exposed the expected pre-E2 nullability failure; the runner-output prerequisite issue is an environment/build prerequisite, not a defect in the added assertions.

## Verdicts

### Spec compliance: APPROVED

The tests encode the required E2 contract: required `Orders.CustomerProfileId`, required idempotency actor and target profile IDs, actor/key idempotency identity, and retained nullable `SubjectId`. The task does not implement production behavior prematurely.

### Task quality: APPROVED

The tests are focused, readable, appropriately named, and use both migration-source and live SQL metadata checks. Assertions are deterministic and scoped to the intended schema transition. `git diff --check` is clean.

## Final verdict

APPROVED
