# Sprint 4B R2 Ownership Enforcement Design

## Goal

Make the ownership written by Sprint 4A D1 mandatory in Azure SQL without changing D1 API behaviour. R2 is a migration-only schema transition followed by an unchanged D1-compatible API rebuild. It is independently deployable to development and test; production is excluded.

## Options considered

1. Enforce ownership in the API only. This leaves a database integrity gap and is rejected.
2. Make the columns required and replace the legacy idempotency key in one E2 migration. This is selected because the prior D1 transition has already established owner data and it makes the database the authority boundary.
3. Remove `SubjectId` now. This is rejected: D1 still reads and writes it. Its removal belongs to R4 after the D2 rollback soak.

## Design

Before each environment migration, temporarily quiesce order traffic and run one transaction that proves: no `Orders` row has a null `CustomerProfileId`; no idempotency row has null actor or target ownership; and no duplicate `(ActorCustomerProfileId, IdempotencyKey)` pair exists. Any failed precondition aborts the release without changing schema.

`EnforceCustomerProfileOwnership` makes the three profile identifiers non-null, changes idempotency identity from `(SubjectId, IdempotencyKey)` to `(ActorCustomerProfileId, IdempotencyKey)`, and retains `SubjectId` as nullable. Restrictive profile foreign keys remain. D1 continues to calculate, read, and write the deterministic legacy subject, so its create/get/replay behaviour is unchanged on E2.

## Release and rollback

R2 records the D1 image/revision before migration, polls the exact migration execution, deploys the D1-compatible rebuild, and proves health, create, replay, direct SQL constraints, and foreign-resource denial. The only rollback is the retained authenticated D1 image or fail-closed order ingress; Sprint 3 is never eligible. The EF `Down` method is model-shape metadata, not an operational rollback: it restores E1's non-null subject primary key and therefore must never be executed in an environment containing nullable R2 subjects. H1 cannot begin until R2 passes targeted QA in test.

The release manifest is deliberately one-shot. It remains present while the exact R2 commit is promoted from development to test, then a follow-up cleanup PR removes it only after test acceptance so routine deployments do not repeat the ingress-quiescing migration path.

## Verification

Integration tests cover E1-to-E2 compatibility, duplicate actor/key rejection, required ownership, and unchanged D1 replay semantics. Generated SQL is reviewed for the intended constraint transition. Development smoke and test QA inspect the migration history, null counts, foreign keys, idempotency key, active digest, and rollback proof.
