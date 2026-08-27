# Azure SQL contained-user bootstrap

Run this once after the non-production Azure SQL server and database have deployed, and again only when an identity is recreated. It creates the two Microsoft Entra contained database users required by CloudOrders; it does not create SQL logins or passwords.

## Prerequisites

- Sign in to Azure CLI as `robMagowan_Az1@outlook.com` in tenant `326531e9-719c-4357-a76f-ef41252ce07e`.
- That user remains the temporary Microsoft Entra administrator for the Azure SQL logical server. Do not replace the subscription Owner assignment, Entra Global Administrator role, or portal account.
- Install the current `SqlServer` PowerShell module for `Invoke-Sqlcmd` with access-token support. The script obtains a short-lived `https://database.windows.net/` token from Azure CLI and never writes it to output.
- The temporary `AllowAllWindowsAzureIps` rule permits Azure Container Apps connectivity only during Sprint 3. Its owner is Robert Magowan, it expires on 2026-09-10, and Sprint 7 must replace it with private networking and remove the rule.

## Run

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ops\Bootstrap-CloudOrdersSql.ps1 `
  -EnvironmentName development `
  -ResourceGroupName ordersapp-development `
  -ServerName <sql-server-name> `
  -DatabaseName CloudOrders `
  -ApiIdentityName cloudorders-dev-api `
  -MigrationIdentityName cloudorders-dev-migrator
```

Use `-WhatIf` to print the idempotent T-SQL before execution. The API identity receives only `db_datareader` and `db_datawriter`; it never receives `db_owner`. The migration identity receives `db_ddladmin`, `db_datareader`, and `db_datawriter` so EF Core can create and update the migration history.

The script is intentionally rejected for `production`. A future automation that creates Entra users from a service principal requires a SQL logical-server managed identity with Microsoft Graph directory-read permissions; this bootstrap instead uses the delegated permissions of the signed-in Entra SQL administrator.
