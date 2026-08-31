# Sprint 4 identity data transition

This is the approval and execution record for the non-production ownership transition. Before the authenticated release receives traffic, the named data-transition approver selects and records either `reset` for disposable data or `mapped-backfill` with a protected one-to-one mapping. Quiesce order ingress, allow in-flight requests to drain, and record the pre-transition inventory in the protected Sprint 4A evidence store.

For a reset, delete rows in FK-safe order: idempotency records, outbox messages, orders, then unreferenced customer profiles. For a mapped backfill, validate each protected mapping, create or reconcile profiles, assign every order owner, and delete legacy idempotency records that cannot establish the new actor. Inside the transaction, prove zero null ownership rows, zero unmapped orders, zero legacy idempotency rows, and no duplicate future actor/key values.

Immediately deploy the compatible authenticated revision and restore ingress only after its health and ownership smoke checks succeed. The prior Sprint 3 API must not be restored after the transition. Store commands, release digest, revision, approved decision, counts, and re-test results in protected evidence; do not copy production-like data, identifiers, or credentials to this repository.
