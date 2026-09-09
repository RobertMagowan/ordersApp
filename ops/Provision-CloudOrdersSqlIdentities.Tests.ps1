$scriptPath = Join-Path $PSScriptRoot 'Provision-CloudOrdersSqlIdentities.ps1'

Describe 'Provision-CloudOrdersSqlIdentities' {
    It 'emits contained-user DDL and validates the expected Entra application IDs' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-dev-api -ApiApplicationId e7957993-59a3-4ce8-a450-cefa415a2890 -MigrationIdentityName cloudorders-dev-migrator -MigrationApplicationId e7714292-e53f-4492-9f27-472ac02a03b4 -WhatIf
        $sql = $output -join "`n"

        $sql | Should Match 'CREATE USER \[cloudorders-dev-api\] WITH SID'
        $sql | Should Match 'CREATE USER \[cloudorders-dev-migrator\] WITH SID'
        $sql | Should Match 'E7957993-59A3-4CE8-A450-CEFA415A2890'
        $sql | Should Match 'E7714292-E53F-4492-9F27-472AC02A03B4'
        $sql | Should Match 'db_ddladmin'
        $sql | Should Match 'BEGIN TRANSACTION'
        $sql | Should Match 'COMMIT TRANSACTION'
    }
}
