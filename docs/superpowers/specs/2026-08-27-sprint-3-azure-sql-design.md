# Sprint 3 Azure SQL Deployment Design

## Goal

Make the durable SQL order/idempotency API deployable to development and test without database passwords or application-startup migrations.

## Chosen design

Each non-production resource group receives an AVM-backed Azure SQL logical server and one `CloudOrders` database in UK West. Development and test use General Purpose serverless compute with a 0.5–1 vCore range and a 60-minute auto-pause delay. Production is unchanged.

The server enables Microsoft Entra-only authentication. `Robert Magowan` is the temporary Entra administrator used only to bootstrap contained users. The API keeps its existing system-assigned managed identity; a separate user-assigned migration identity is attached to a one-shot Container Apps Job. Both connect using `Authentication=Active Directory Managed Identity`; the application receives a non-secret `ConnectionStrings__CloudOrders` value containing only server, database, encryption, and authentication settings.

The bootstrap script, run explicitly by the administrator, creates database users and least-privilege roles: API gets data read/write plus execution permissions; the migration identity gets schema migration permissions. The API never receives `db_owner`. The migration job runs the committed migration bundle before the API candidate revision is released; `CloudOrders.Api` still never calls `Database.Migrate()`.

## Network and temporary exception

Container Apps does not yet have private VNet integration. Azure SQL therefore retains public network access only with the `AllowAllWindowsAzureIps` firewall rule required for Azure-hosted migration and runtime traffic. The rule is tagged/documented with owner `Robert Magowan`, expiry `2026-09-10`, and mandatory Sprint 7 removal when private endpoints are introduced. TLS is 1.2 or later; public access, Entra-only authentication, and the exception are verified in deployment evidence.

## Deployment flow

`deploy.yml` validates and previews SQL changes, builds/publishes immutable API and migration images, applies the AVM composition, starts the migration Job, verifies its completed execution, and only then updates the API image. Candidate readiness, TLS health, immutable digest identity, migration history, and first-use/replay/conflict database state form the development smoke gate. Test promotion repeats the same artifact and adds QA-only destructive verification.

## Safety and acceptance

No password, token, firewall IP address, or real order data is committed. The dedicated bootstrap script refuses a production resource group and emits sanitized evidence. Failure of SQL provisioning, bootstrap, migration, or readiness prevents API promotion; the existing ready revision retains traffic. The design follows Azure SQL guidance for Entra-only servers and managed identities, and serverless auto-pause behavior.
