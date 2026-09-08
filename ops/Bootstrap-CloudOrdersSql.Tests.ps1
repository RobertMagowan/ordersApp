$scriptPath = Join-Path $PSScriptRoot 'Bootstrap-CloudOrdersSql.ps1'

Describe 'Bootstrap-CloudOrdersSql' {
    It 'rejects production before connecting to Azure SQL' {
        $threw = $false
        try {
            & $scriptPath -EnvironmentName production -ResourceGroupName ordersapp-production -ServerName cloudorders-prod-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-prod-api -MigrationIdentityName cloudorders-prod-migrator -WhatIf
        }
        catch {
            $threw = $true
        }

        $threw | Should Be $true
    }

    It 'requires non-empty resource identifiers' {
        $threw = $false
        try {
            & $scriptPath -EnvironmentName development -ResourceGroupName '' -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -ApiIdentityName cloudorders-dev-api -MigrationIdentityName cloudorders-dev-migrator -WhatIf
        }
        catch {
            $threw = $true
        }

        $threw | Should Be $true
    }

    It 'emits only the read-only ownership precondition for CI' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -WhatIf
        $sql = $output -join "`n"

        $sql | Should Match 'BEGIN TRANSACTION'
        $sql | Should Not Match 'CREATE USER'
        $sql | Should Not Match 'ALTER ROLE'
    }

    It 'emits a one-transaction read-only ownership precondition probe' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -WhatIf
        $sql = $output -join "`n"

        $sql | Should Match '(?is)BEGIN TRANSACTION.*Orders.*CustomerProfileId.*ROLLBACK TRANSACTION'
        $sql | Should Match '(?is)Idempotency.*ActorCustomerProfileId.*TargetCustomerProfileId'
        $sql | Should Match '(?is)GROUP BY.*ActorCustomerProfileId.*IdempotencyKey.*HAVING COUNT'
        $sql | Should Not Match '(?is)(UPDATE|INSERT|DELETE)\s+(Orders|IdempotencyRecords)'
    }

    It 'emits SQL-compatible comments in the ownership precondition' {
        $output = & $scriptPath -EnvironmentName development -ResourceGroupName ordersapp-development -ServerName cloudorders-dev-sql -DatabaseName CloudOrders -WhatIf
        $sql = $output -join "`n"

        $sql | Should Not Match '(?m)^\s*#'
    }

    It 'fails closed when the ownership precondition reports unsafe rows' {
        $script = Get-Content $scriptPath -Raw
        $script | Should Match 'Precondition'
        $script | Should Match '(?is)non-zero.*block|block.*non-zero'
        $script | Should Match '(?is)throw.*ownership'
    }
}
