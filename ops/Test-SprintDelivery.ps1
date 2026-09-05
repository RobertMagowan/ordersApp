[CmdletBinding()]
param()

Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testPath = Join-Path $repositoryRoot 'ops/tests/SprintDelivery.Tests.ps1'
$result = Invoke-Pester -Script $testPath -PassThru

if ($result.FailedCount -gt 0) {
    exit 1
}
