# Release Migration Manifest Design

## Goal

Make schema required by every non-production release explicit and immutable. An API deployment cannot omit a required migration, and retry logic must never infer readiness from a Git diff.

## Immutable release descriptor

Each deployable commit contains one canonical descriptor at `ops/releases/current-release.json`. The workflow selects it solely from the checked-out immutable release SHA, including manual dispatches and retries. It declares:

- a release ID and schema version;
- whether its API is deployed and the permitted environments;
- the complete ordered EF migration baseline required after deployment;
- the cumulative, ordered migration authorisation for that baseline; and
- allowlisted preconditions, traffic policy, and a compatibility declaration.

A code-only release adds no migration authorisations but retains the cumulative authorisation and required baseline. The workflow reads the target history, requires it to be a permitted prefix of the baseline, derives its outstanding suffix, and requires every outstanding migration to be authorised. Unknown history, holes, or a database ahead of the descriptor fail closed. It never selects work from a Git diff.

## Controlled migration execution

The migration executable gains two explicit modes. `--verify-release` resolves full EF migration IDs and reports a sanitised ordered history and baseline. `--apply-release` derives the authorised outstanding suffix, applies only that ordered suffix, then verifies the observed delta and baseline. Baseline equality is the verified no-op.

The existing migration Job receives the descriptor hash, release SHA, image digest, and mode. It rejects mismatched environment, database, image, or arguments. Pipeline evidence records migration IDs, execution name and outcome, and sanitised history. The Job identity remains the only SQL principal used by the pipeline.

The descriptor supports cumulative `development` to `test` promotions with an ordered multi-migration plan. It must never fall back to `Database.MigrateAsync`.

## Preconditions and recovery

Preconditions use an allowlisted registry: `ownership-read-only-transaction` retains the existing check and `none` performs no check; unknown values fail closed. For `deployApi: false`, the workflow verifies the API revision, image digest, and traffic are unchanged. Historic Sprint 4 manifests are translated only as compatibility data and never run unrelated controls.

A timeout is an uncertain remote result. Before retrying, the workflow reconciles the prior Job execution and schema verification; it blocks API deployment until the outcome is unambiguous. A matching, already-applied plan is a verified no-op. There is no automatic `Down` migration. Incompatible migrations require a declared maintenance/compatibility strategy.

The first descriptor declares `AddOutboxLeasing` for development and test as API-compatible; production remains excluded.

## Validation

Workflow contracts cover malformed or ambiguous descriptors, manual dispatch, code-only-after-migration promotion, ordered cumulative plans, partial-plan recovery, wrong digest/arguments/database, unknown history or holes, unknown policies, already-applied plans, timeout reconciliation, and API-unchanged releases. Development evidence must record migration IDs/history, outbox lease columns, and live revision before Task 1 closes.
