[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateSet('development', 'test')][string]$EnvironmentName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ResourceGroupName,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,63}$')][string]$ServerName,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,127}$')][string]$DatabaseName,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,127}$')][string]$ApiIdentityName,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F-]{36}$')][Guid]$ApiApplicationId,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,127}$')][string]$MigrationIdentityName,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F-]{36}$')][Guid]$MigrationApplicationId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Quote-SqlIdentifier([string]$Name) { "[$($Name.Replace(']', ']]'))]" }

$apiIdentity = Quote-SqlIdentifier $ApiIdentityName
$migrationIdentity = Quote-SqlIdentifier $MigrationIdentityName
$apiId = $ApiApplicationId.ToString().ToUpperInvariant()
$migrationId = $MigrationApplicationId.ToString().ToUpperInvariant()
$apiSidHex = [BitConverter]::ToString($ApiApplicationId.ToByteArray()).Replace('-', '')
$migrationSidHex = [BitConverter]::ToString($MigrationApplicationId.ToByteArray()).Replace('-', '')
$sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$ApiIdentityName' AND CONVERT(varchar(36), CAST(sid AS uniqueidentifier)) <> N'$apiId') THROW 51002, 'API identity object ID does not match the expected managed identity.', 1;
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$MigrationIdentityName' AND CONVERT(varchar(36), CAST(sid AS uniqueidentifier)) <> N'$migrationId') THROW 51003, 'Migration identity object ID does not match the expected managed identity.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$ApiIdentityName')
    CREATE USER $apiIdentity WITH SID = 0x$apiSidHex, TYPE = E;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$MigrationIdentityName')
    CREATE USER $migrationIdentity WITH SID = 0x$migrationSidHex, TYPE = E;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals p ON p.principal_id = m.member_principal_id WHERE r.name = N'db_datareader' AND p.name = N'$ApiIdentityName') ALTER ROLE [db_datareader] ADD MEMBER $apiIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals p ON p.principal_id = m.member_principal_id WHERE r.name = N'db_datawriter' AND p.name = N'$ApiIdentityName') ALTER ROLE [db_datawriter] ADD MEMBER $apiIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals p ON p.principal_id = m.member_principal_id WHERE r.name = N'db_ddladmin' AND p.name = N'$MigrationIdentityName') ALTER ROLE [db_ddladmin] ADD MEMBER $migrationIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals p ON p.principal_id = m.member_principal_id WHERE r.name = N'db_datareader' AND p.name = N'$MigrationIdentityName') ALTER ROLE [db_datareader] ADD MEMBER $migrationIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals p ON p.principal_id = m.member_principal_id WHERE r.name = N'db_datawriter' AND p.name = N'$MigrationIdentityName') ALTER ROLE [db_datawriter] ADD MEMBER $migrationIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$ApiIdentityName' AND CONVERT(varchar(36), CAST(sid AS uniqueidentifier)) = N'$apiId') THROW 51002, 'API identity object ID does not match the expected managed identity.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$MigrationIdentityName' AND CONVERT(varchar(36), CAST(sid AS uniqueidentifier)) = N'$migrationId') THROW 51003, 'Migration identity object ID does not match the expected managed identity.', 1;
COMMIT TRANSACTION;
"@

if ($WhatIfPreference) { Write-Output $sql; return }
if (-not (Get-Command az -ErrorAction SilentlyContinue) -or -not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) { throw 'Azure CLI and Invoke-Sqlcmd are required for administrator provisioning.' }
$token = az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Azure CLI did not return a Microsoft Entra SQL access token.' }
$serverFqdn = "$ServerName.database.windows.net"
if ($PSCmdlet.ShouldProcess("$serverFqdn/$DatabaseName", 'provision CloudOrders contained managed-identity users and roles')) {
    Invoke-Sqlcmd -ServerInstance $serverFqdn -Database $DatabaseName -AccessToken $token -Query $sql -AbortOnError
}
