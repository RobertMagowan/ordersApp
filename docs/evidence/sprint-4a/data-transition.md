# Sprint 4A development data transition

## Scope

This record covers only the disposable development database:

- Resource group: `ordersapp-development`
- SQL server: `cloudordersd583431devsql`
- Database: `CloudOrders`
- Decision: `reset`

## Preconditions

The Azure Query Editor verified the E1 migration
`20260829075044_AddCustomerProfileOwnershipExpand` is installed. Before the
reset, the disposable data contained one `Orders` row, one `OutboxMessages`
row, one `IdempotencyRecords` row, and zero `CustomerProfiles` rows.

The foreign-key inspection confirmed this safe deletion order:

1. `IdempotencyRecords`
2. `OutboxMessages`
3. `Orders`
4. `CustomerProfiles`

## Execution and verification

The reset ran in one SQL transaction and deleted three rows. No schema or
migration-history rows were changed. The post-transaction count query returned
zero for all four tables.

The E1 migration-only manifest is removed in this D1 release candidate. Its
removal restores the normal protected-branch migration, image, candidate, and
health-check workflow; it does not re-enable the pre-D1 Sprint 3 revision.

## Remaining release gate

The D1 candidate must be reviewed, merged to `development`, and shown healthy
before application ingress is treated as restored. The resulting immutable
revision, image digest, migration execution, and development smoke evidence
belong in the release evidence before promotion to `test`.
