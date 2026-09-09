Describe 'Non-production cost controls' -Tag 'infrastructure' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $mainTemplate = Get-Content -Raw (Join-Path $repositoryRoot 'infra/main.bicep')
        $developmentParameters = Get-Content -Raw (Join-Path $repositoryRoot 'infra/environments/development.bicepparam')
        $testParameters = Get-Content -Raw (Join-Path $repositoryRoot 'infra/environments/test.bicepparam')
        $productionParameters = Get-Content -Raw (Join-Path $repositoryRoot 'infra/environments/production.bicepparam')
        $sqlModule = Get-Content -Raw (Join-Path $repositoryRoot 'infra/modules/sql-database.bicep')
        $deploymentWorkflow = Get-Content -Raw (Join-Path $repositoryRoot '.github/workflows/deploy.yml')
    }

    It 'uses scale-to-zero and a fifteen minute serverless SQL pause period' {
        $mainTemplate | Should Match 'param minReplicas int = 1'
        $developmentParameters | Should Match 'param minReplicas = 0'
        $testParameters | Should Match 'param minReplicas = 0'
        $productionParameters | Should Match 'param minReplicas = 1'
        $sqlModule | Should Match 'autoPauseDelay: 15'
        $sqlModule | Should Match "name: 'GP_S_Gen5_1'"
        $sqlModule | Should Match "minCapacity: '0.5'"
    }

    It 'keeps bounded candidate and health retries for on-demand startup' {
        $deploymentWorkflow | Should Match 'Wait for candidate revision'
        $deploymentWorkflow | Should Match 'for attempt in \{1\.\.30\}'
        $deploymentWorkflow | Should Match '/health/live'
    }
}
