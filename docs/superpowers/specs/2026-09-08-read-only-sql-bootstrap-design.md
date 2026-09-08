# Read-only SQL Bootstrap Design

## Decision

The GitHub Actions OIDC identity remains read-only in Azure SQL. The deployment workflow runs the ownership-integrity precondition only; it must not create contained users or change role membership.

## Rationale

The API and migration managed identities are already provisioned by Azure and have their required database roles. Allowing the general deployment identity to repair SQL identity state would require role-management permissions and expand its blast radius. Metadata-read permission alone would not repair drift and would not solve missing users.

## Boundaries

- `Bootstrap-CloudOrdersSql.ps1` emits and executes only the transaction-backed ownership precondition during normal CI deployment.
- Provisioning or repairing managed-identity SQL users/roles is an administrator-controlled operation, outside the GitHub deployment path. It uses a dedicated script and validates each contained user's Entra application ID in `sys.database_principals.sid`, not its display name alone.
- The deployment path does not silently repair drift. An administrator preflight validates the API and migration principals, their application IDs, and exact role memberships before a new environment is admitted or a recreated identity is used.
- Development and test remain covered; production remains excluded.

## Verification

Architecture tests must assert the CI step cannot invoke user or role DDL. Pester mocks `Invoke-Sqlcmd` to prove the deployment script submits exactly one ownership-precondition query, and static checks reject `CREATE USER` and `ALTER ROLE` in that script. The administrator script must be separately tested for object-ID matching and exact roles. The next development deployment must pass the ownership precondition and start the migration job without changing CI SQL privileges.
