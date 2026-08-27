$scriptPath = Join-Path $PSScriptRoot 'Bootstrap-CloudOrdersSql.ps1'

Describe 'Bootstrap-CloudOrdersSql' {
    It 'rejects production before connecting to Azure SQL' {
        { & $scriptPath -EnvironmentName production -ResourceGroupName ordersapp-production -ServerName cloudorders-prod-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-prod-api -MigrationIdentityName cloudorders-prod-migrator -WhatIf } |
            Should Throw
    }

    It 'requires non-empty resource identifiers' {
        { & $scriptPath -EnvironmentName development -ResourceGroupName '' -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-dev-api -MigrationIdentityName cloudorders-dev-migrator -WhatIf } |
            Should Throw
    }

    It 'emits least-privilege contained-user SQL without API db_owner' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-dev-api -MigrationIdentityName cloudorders-dev-migrator -WhatIf
        $sql = $output -join "`n"

        $sql | Should Match 'CREATE USER \[cloudorders-dev-api\] FROM EXTERNAL PROVIDER'
        $sql | Should Match 'ALTER ROLE \[db_datareader\] ADD MEMBER \[cloudorders-dev-api\]'
        $sql | Should Match 'ALTER ROLE \[db_ddladmin\] ADD MEMBER \[cloudorders-dev-migrator\]'
        $sql | Should Not Match 'db_owner.*cloudorders-dev-api'
    }
}
