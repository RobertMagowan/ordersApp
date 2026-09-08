# Read-only SQL Bootstrap Design

## Decision

The GitHub Actions OIDC identity remains read-only in Azure SQL. The deployment workflow runs the ownership-integrity precondition only; it must not create contained users or change role membership.

## Rationale

The API and migration managed identities are already provisioned by Azure and have their required database roles. Allowing the general deployment identity to repair SQL identity state would require role-management permissions and expand its blast radius. Metadata-read permission alone would not repair drift and would not solve missing users.

## Boundaries

- `Bootstrap-CloudOrdersSql.ps1` emits and executes only the transaction-backed ownership precondition during normal CI deployment.
- Provisioning or repairing managed-identity SQL users/roles is an administrator-controlled operation, outside the GitHub deployment path.
- A missing role will fail later at the managed identity's real database operation rather than being silently repaired by CI.
- Development and test remain covered; production remains excluded.

## Verification

Architecture tests must assert the CI step cannot invoke user or role DDL. Pester must assert the deployment-mode script emits no `CREATE USER` or `ALTER ROLE`. The next development deployment must pass the ownership precondition and start the migration job without changing CI SQL privileges.
