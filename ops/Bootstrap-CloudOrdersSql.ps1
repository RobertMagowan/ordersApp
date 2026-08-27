[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$EnvironmentName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]{1,63}$')]
    [string]$ServerName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,127}$')]
    [string]$DatabaseName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]{1,127}$')]
    [string]$ApiIdentityName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]{1,127}$')]
    [string]$MigrationIdentityName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($EnvironmentName -eq 'production') {
    throw 'production is not supported by the CloudOrders SQL bootstrap.'
}

if ($EnvironmentName -notin @('development', 'test')) {
    throw 'EnvironmentName must be development or test.'
}

function Quote-SqlIdentifier {
    param([Parameter(Mandatory)][string]$Name)

    return "[$($Name.Replace(']', ']]'))]"
}

$apiIdentity = Quote-SqlIdentifier $ApiIdentityName
$migrationIdentity = Quote-SqlIdentifier $MigrationIdentityName
$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$ApiIdentityName')
    CREATE USER $apiIdentity FROM EXTERNAL PROVIDER;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$MigrationIdentityName')
    CREATE USER $migrationIdentity FROM EXTERNAL PROVIDER;

IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_datareader' AND m.name = N'$ApiIdentityName')
    ALTER ROLE [db_datareader] ADD MEMBER $apiIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_datawriter' AND m.name = N'$ApiIdentityName')
    ALTER ROLE [db_datawriter] ADD MEMBER $apiIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_ddladmin' AND m.name = N'$MigrationIdentityName')
    ALTER ROLE [db_ddladmin] ADD MEMBER $migrationIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_datareader' AND m.name = N'$MigrationIdentityName')
    ALTER ROLE [db_datareader] ADD MEMBER $migrationIdentity;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_datawriter' AND m.name = N'$MigrationIdentityName')
    ALTER ROLE [db_datawriter] ADD MEMBER $migrationIdentity;
"@

if ($WhatIfPreference) {
    Write-Output $sql
    return
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Sign in with the temporary Microsoft Entra SQL administrator first.'
}

if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'Invoke-Sqlcmd from the SqlServer PowerShell module is required. Install-Module SqlServer -Scope CurrentUser.'
}

$accessToken = az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'Azure CLI did not return a Microsoft Entra SQL access token.'
}

$serverFqdn = "$ServerName.database.windows.net"
if ($PSCmdlet.ShouldProcess("$serverFqdn/$DatabaseName", 'create CloudOrders contained users and least-privilege roles')) {
    Invoke-Sqlcmd -ServerInstance $serverFqdn -Database $DatabaseName -AccessToken $accessToken -Query $sql -AbortOnError
}
