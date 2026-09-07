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

    It 'emits a one-transaction read-only ownership precondition probe' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-dev-api -MigrationIdentityName cloudorders-dev-migrator -WhatIf
        $sql = $output -join "`n"

        $sql | Should Match '(?is)BEGIN TRANSACTION.*Orders.*CustomerProfileId.*ROLLBACK TRANSACTION'
        $sql | Should Match '(?is)Idempotency.*ActorCustomerProfileId.*TargetCustomerProfileId'
        $sql | Should Match '(?is)GROUP BY.*ActorCustomerProfileId.*IdempotencyKey.*HAVING COUNT'
        $sql | Should Not Match '(?is)(UPDATE|INSERT|DELETE)\s+(Orders|IdempotencyRecords)'
    }

    It 'fails closed when the ownership precondition reports unsafe rows' {
        $script = Get-Content $scriptPath -Raw
        $script | Should Match 'Precondition'
        $script | Should Match '(?is)non-zero.*block|block.*non-zero'
        $script | Should Match '(?is)throw.*ownership'
    }
}
