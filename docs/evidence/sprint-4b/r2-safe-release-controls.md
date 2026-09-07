# Sprint 4B R2 safe release controls

The R2 release is restricted to `development` and `test`. Before the E2 migration, the workflow captures the active D1 revision and immutable image digest, quiesces ingress, and invokes the read-only ownership precondition. The precondition runs in one transaction and rolls back; non-zero null-order, null-actor, null-target, or duplicate actor/key counts throw and stop the release. It contains no data repair, backfill, or deletion statements.

After the exact `EnforceCustomerProfileOwnership` migration execution succeeds, the API candidate must pass health before ingress traffic is restored. Production is explicitly refused and the Sprint 4A D1 image/revision remains the rollback reference.
