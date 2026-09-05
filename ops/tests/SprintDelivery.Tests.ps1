Describe 'Sprint delivery contracts' -Tag 'contracts' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $config = Get-Content -Raw (Join-Path $repositoryRoot 'delivery/config.json') | ConvertFrom-Json
        $stateSchema = Get-Content -Raw (Join-Path $repositoryRoot 'delivery/schemas/state.schema.json') | ConvertFrom-Json
        $configSchema = Get-Content -Raw (Join-Path $repositoryRoot 'delivery/schemas/config.schema.json') | ConvertFrom-Json
        $evidenceSchema = Get-Content -Raw (Join-Path $repositoryRoot 'delivery/schemas/evidence.schema.json') | ConvertFrom-Json

        function Get-DeliveryFixture {
            param([Parameter(Mandatory)][string] $Name)

            $fixture = Get-Content -Raw (Join-Path $repositoryRoot 'delivery/state.json') | ConvertFrom-Json
            $workItem = $fixture.currentSprint.workItems[0]

            switch ($Name) {
                'merged-with-failed-ci' {
                    $workItem.lifecycle = 'MERGED'
                    $workItem.gates.ci.status = 'FAIL'
                }
                'task-done-with-pending-review' {
                    $workItem.lifecycle = 'MERGED'
                    $workItem.gates.codeReview.status = 'PENDING'
                }
                'missing-stage' { $workItem.PSObject.Properties.Remove('stage') }
                'invalid-evidence-binding' {
                    $workItem.evidenceBindings = @([pscustomobject]@{ commit = 'not-a-commit'; status = 'CURRENT' })
                }
                'missing-retry-counter' { $workItem.retryCounters.PSObject.Properties.Remove('deployment') }
                'invalid-blocker' {
                    $workItem.blockers = @([pscustomobject]@{ status = 'HUMAN_DECISION_REQUIRED' })
                }
                'imported-sprint-4a' { }
                default { throw "Unknown delivery fixture '$Name'." }
            }

            return $fixture
        }

        function Assert-ContractFixtureStructure {
            param([Parameter(Mandatory)] $State)

            foreach ($field in 'workflowVersion', 'stateSchemaVersion', 'configurationVersion', 'currentSprint') {
                if ($field -notin $State.PSObject.Properties.Name) { throw "State is missing '$field'." }
            }

            foreach ($workItem in $State.currentSprint.workItems) {
                foreach ($field in 'stage', 'evidenceBindings', 'retryCounters', 'blockers', 'gates') {
                    if ($field -notin $workItem.PSObject.Properties.Name) { throw "Work item is missing '$field'." }
                }

                if ($workItem.stage -notin $config.vocabulary.stage) {
                    throw "Unknown stage '$($workItem.stage)'."
                }

                foreach ($binding in $workItem.evidenceBindings) {
                    if ($binding.status -notin @('CURRENT', 'STALE', 'HISTORICAL_UNVERIFIED', 'SUPERSEDED')) {
                        throw "Unknown evidence status '$($binding.status)'."
                    }
                    if ($binding.commit -and $binding.commit -notmatch '^[0-9a-f]{7,40}$') {
                        throw "Invalid evidence commit '$($binding.commit)'."
                    }
                }

                foreach ($counter in 'command', 'deployment') {
                    if ($counter -notin $workItem.retryCounters.PSObject.Properties.Name -or $workItem.retryCounters.$counter -lt 0) {
                        throw "Invalid retry counter '$counter'."
                    }
                }

                foreach ($blocker in $workItem.blockers) {
                    if ($blocker.status -notin $config.vocabulary.blockerStatus -or
                        'reason' -notin $blocker.PSObject.Properties.Name -or
                        [string]::IsNullOrWhiteSpace($blocker.reason)) {
                        throw 'Invalid blocker.'
                    }
                }
            }
        }
    }

    It 'loads all three JSON schemas with required structural contracts' {
        foreach ($schema in @($configSchema, $stateSchema, $evidenceSchema)) {
            $schema.'$schema' | Should Be 'https://json-schema.org/draft/2020-12/schema'
            $schema.required | Should Not BeNullOrEmpty
        }

        ($stateSchema.'$defs'.workItem.required -contains 'stage') | Should Be $true
        ($stateSchema.'$defs'.workItem.required -contains 'evidenceBindings') | Should Be $true
        ($stateSchema.'$defs'.workItem.required -contains 'retryCounters') | Should Be $true
        ($stateSchema.'$defs'.workItem.required -contains 'blockers') | Should Be $true
        $stateSchema.'$defs'.evidenceBinding.properties.commit.pattern | Should Be '^[0-9a-f]{7,40}$'
    }

    It 'rejects a malformed state schema missing required work-item structure' {
        $malformedSchema = $stateSchema | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $malformedSchema.'$defs'.workItem.properties.PSObject.Properties.Remove('stage')

        {
            foreach ($field in 'stage', 'evidenceBindings', 'retryCounters', 'blockers') {
                if ($field -notin $malformedSchema.'$defs'.workItem.properties.PSObject.Properties.Name) {
                    throw "State schema is missing '$field'."
                }
            }
        } | Should Throw
    }

    It 'rejects a state with a lifecycle/gate collision' {
        $state = Get-DeliveryFixture 'merged-with-failed-ci'

        {
            $item = $state.currentSprint.workItems[0]
            if ($item.lifecycle -eq 'MERGED' -and $item.gates.ci.status -eq 'FAIL') {
                throw 'Lifecycle conflicts with failed ci gate.'
            }
        } | Should Throw
    }

    It 'rejects completion while a required gate is pending' {
        $state = Get-DeliveryFixture 'task-done-with-pending-review'

        {
            $item = $state.currentSprint.workItems[0]
            foreach ($gateName in $config.requiredGatesByRisk.($item.risk)) {
                if ($item.gates.$gateName.status -notin @('PASS', 'NOT_APPLICABLE')) {
                    throw "Required gate '$gateName' is '$($item.gates.$gateName.status)'."
                }
            }
        } | Should Throw
    }

    It 'rejects malformed stage, evidence, retry, and blocker fixtures' {
        foreach ($fixtureName in 'missing-stage', 'invalid-evidence-binding', 'missing-retry-counter', 'invalid-blocker') {
            $state = Get-DeliveryFixture $fixtureName
            { Assert-ContractFixtureStructure -State $state } | Should Throw
        }
    }

    It 'records imported code-review and CI gates as historical unless directly bound' {
        $state = Get-DeliveryFixture 'imported-sprint-4a'
        $importedItems = $state.currentSprint.workItems | Where-Object { $_.id -in @('4A-1', '4A-2', '4A-3', '4A-6', '4A-E1') }

        foreach ($item in $importedItems) {
            $item.gates.codeReview.status | Should Be 'HISTORICAL_UNVERIFIED'
            $item.gates.ci.status | Should Be 'HISTORICAL_UNVERIFIED'
        }

        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings.workflowRun | Should Be 33457927112
        $e1.evidenceBindings.status | Should Be 'HISTORICAL_UNVERIFIED'
        $e1.gates.devValidation.status | Should Be 'HISTORICAL_UNVERIFIED'
    }

    It 'accepts the imported Sprint 4A structural state' {
        $state = Get-DeliveryFixture 'imported-sprint-4a'

        { Assert-ContractFixtureStructure -State $state } | Should Not Throw
    }
}

Describe 'Sprint delivery completion' -Tag 'completion' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        . (Join-Path $repositoryRoot 'ops/Invoke-SprintDelivery.ps1')
        $config = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/config.json')

        function Get-CompletionFixture {
            param([Parameter(Mandatory)][string] $Name)

            $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')

            switch ($Name) {
                'all-required-gates-pass' {
                    $item = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-4' }
                    foreach ($gateName in $config.requiredGatesByRisk.($item.risk)) {
                        $item.gates.$gateName.status = 'PASS'
                    }

                    return $item
                }
                'decision-blocked' {
                    $state.currentSprint.workItems = @($state.currentSprint.workItems | Where-Object { $_.id -eq '4A-7-D1' })
                    return $state
                }
                default { throw "Unknown completion fixture '$Name'." }
            }
        }
    }

    It 'derives task completion from every required risk gate' {
        $item = Get-CompletionFixture 'all-required-gates-pass'

        (Assert-TaskDone -WorkItem $item -Config $config) | Should Be $true
        $item.gates.devValidation.status = 'STALE'

        $failure = $null
        try { Assert-TaskDone -WorkItem $item -Config $config } catch { $failure = $_.Exception.Message }
        $failure | Should Match 'STALE'
    }

    It 'does not trust a declared release lifecycle when a required gate is pending' {
        $item = Get-CompletionFixture 'all-required-gates-pass'
        $item.lifecycle = 'RELEASED'
        $item.gates.qaValidation.status = 'PENDING'

        $failure = $null
        try { Assert-TaskDone -WorkItem $item -Config $config } catch { $failure = $_.Exception.Message }
        $failure | Should Match 'qaValidation.*PENDING'
    }

    It 'returns a human decision action for a decision-blocked work item' {
        $state = Get-CompletionFixture 'decision-blocked'

        $action = Get-NextDeliveryAction -State $state -Config $config

        $action.kind | Should Be 'HUMAN_DECISION_REQUIRED'
        $action.workItemId | Should Be '4A-7-D1'
        $action.reason | Should Match 'External ID tenant'
    }
}
