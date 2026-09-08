$scriptPath = Join-Path $PSScriptRoot 'Provision-CloudOrdersSqlIdentities.ps1'

Describe 'Provision-CloudOrdersSqlIdentities' {
    It 'emits contained-user DDL and validates the expected Entra application IDs' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-dev-api -ApiObjectId 531c7fce-73c2-491e-9422-1f8713e2993a -MigrationIdentityName cloudorders-dev-migrator -MigrationObjectId 3c5c53fe-e390-4754-a719-f31cf786ab8b -WhatIf
        $sql = $output -join "`n"

        $sql | Should Match 'CREATE USER \[cloudorders-dev-api\] WITH SID'
        $sql | Should Match 'CREATE USER \[cloudorders-dev-migrator\] WITH SID'
        $sql | Should Match '531C7FCE-73C2-491E-9422-1F8713E2993A'
        $sql | Should Match '3C5C53FE-E390-4754-A719-F31CF786AB8B'
        $sql | Should Match 'db_ddladmin'
        $sql | Should Match 'BEGIN TRANSACTION'
        $sql | Should Match 'COMMIT TRANSACTION'
    }
}
