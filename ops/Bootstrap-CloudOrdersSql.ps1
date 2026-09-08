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
    [string]$DatabaseName

)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($EnvironmentName -eq 'production') {
    throw 'production is not supported by the CloudOrders SQL bootstrap.'
}

if ($EnvironmentName -notin @('development', 'test')) {
    throw 'EnvironmentName must be development or test.'
}

$ownershipPreconditionSql = @'
-- Any non-zero count blocks migration. This transaction never repairs, backfills, or deletes data.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @nullOrderOwnership bigint = (SELECT COUNT_BIG(*) FROM dbo.Orders WHERE CustomerProfileId IS NULL);
DECLARE @nullActorOwnership bigint = (SELECT COUNT_BIG(*) FROM dbo.IdempotencyRecords WHERE ActorCustomerProfileId IS NULL);
DECLARE @nullTargetOwnership bigint = (SELECT COUNT_BIG(*) FROM dbo.IdempotencyRecords WHERE TargetCustomerProfileId IS NULL);
DECLARE @duplicateActorKeyGroups bigint = (
    SELECT COUNT_BIG(*) FROM (
        SELECT ActorCustomerProfileId, IdempotencyKey
        FROM dbo.IdempotencyRecords
        GROUP BY ActorCustomerProfileId, IdempotencyKey
        HAVING COUNT_BIG(*) > 1
    ) AS duplicateGroups
);
IF @nullOrderOwnership <> 0 OR @nullActorOwnership <> 0 OR @nullTargetOwnership <> 0 OR @duplicateActorKeyGroups <> 0
    THROW 51001, 'Sprint 4B ownership precondition failed; migration is blocked.', 1;
ROLLBACK TRANSACTION;
'@
$sql = $ownershipPreconditionSql

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
if ($PSCmdlet.ShouldProcess("$serverFqdn/$DatabaseName", 'run read-only CloudOrders ownership precondition')) {
    Invoke-Sqlcmd -ServerInstance $serverFqdn -Database $DatabaseName -AccessToken $accessToken -Query $ownershipPreconditionSql -AbortOnError
}
