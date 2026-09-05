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
        $state.cutover.status = 'WORKFLOW_CUTOVER_COMPLETE'
        $state.cutover.blockers = [System.Collections.ArrayList]::new()

        $action = Get-NextDeliveryAction -State $state -Config $config

        $action.kind | Should Be 'HUMAN_DECISION_REQUIRED'
        $action.workItemId | Should Be '4A-7-D1'
        $action.reason | Should Match 'External ID tenant'
    }

    It 'selects the earliest candidate before evaluating later blocked work' {
        $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
        $state.cutover.status = 'WORKFLOW_CUTOVER_COMPLETE'
        $state.cutover.blockers = [System.Collections.ArrayList]::new()

        $action = Get-NextDeliveryAction -State $state -Config $config

        $action.kind | Should Be 'WORK_ITEM_READY'
        $action.workItemId | Should Be '4A-4'
    }

    It 'rejects a persisted state with an unsupported state schema version' {
        $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
        $state.stateSchemaVersion = '99.0'

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw
    }

    It 'rejects invalid persisted evidence bindings and retry counters' {
        $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
        $item = $state.currentSprint.workItems[0]
        $item.evidenceBindings[0].status = 'UNKNOWN'

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw

        $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
        $state.currentSprint.workItems[0].retryCounters.command = 3

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw
    }

    It 'rejects an invalid persisted D1 blocker status' {
        $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
        $d1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-7-D1' }
        $d1.blockers[0].status = 'NOT_A_BLOCKER_STATUS'

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw
    }
}

Describe 'Sprint delivery reconciliation' -Tag 'reconciliation' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        . (Join-Path $repositoryRoot 'ops/Invoke-SprintDelivery.ps1')
        $config = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/config.json')

        function Get-ReconciliationFixture {
            $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
            $item = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
            $item.evidenceBindings[0].status = 'CURRENT'
            $item.gates.devValidation.status = 'PASS'
            return $state
        }
    }

    It 'marks deployment evidence stale in a derived state when its immutable commit differs' {
        $state = Get-ReconciliationFixture
        $snapshot = @{ deployments = @(@{ commit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'; workflowRun = 33457927112; environment = 'development' }) }

        $result = Compare-DeliveryState -State $state -Snapshot $snapshot
        $derivedItem = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $originalItem = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }

        $result.kind | Should Be 'STATE_RECONCILIATION_REQUIRED'
        $derivedItem.gates.devValidation.status | Should Be 'STALE'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
        $originalItem.gates.devValidation.status | Should Be 'PASS'
        $originalItem.evidenceBindings[0].status | Should Be 'CURRENT'
    }

    It 'returns no contradiction when an injected deployment snapshot matches every immutable identifier' {
        $state = Get-ReconciliationFixture
        $snapshot = @{ deployments = @(@{ commit = 'fbc68a9f0e02923880c8a06162a8d7cda2afac38'; workflowRun = 33457927112; environment = 'development' }) }

        $result = Compare-DeliveryState -State $state -Snapshot $snapshot

        $result.kind | Should Be 'STATE_RECONCILIATION_AGREES'
        $result.contradictions.Count | Should Be 0
    }

    It 'fails closed when current cloud evidence is missing its commit even if the snapshot is missing it too' {
        $state = Get-ReconciliationFixture
        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings[0].PSObject.Properties.Remove('commit')
        $snapshot = @{ deployments = @(@{ workflowRun = 33457927112; environment = 'development' }) }

        $result = Compare-DeliveryState -State $state -Snapshot $snapshot
        $derivedItem = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }

        $result.kind | Should Be 'STATE_RECONCILIATION_REQUIRED'
        $result.contradictions[0].kind | Should Be 'DEPLOYMENT_EVIDENCE_INCOMPLETE'
        $derivedItem.gates.devValidation.status | Should Be 'STALE'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
    }

    It 'fails closed when current cloud evidence is missing its environment even if the snapshot is missing it too' {
        $state = Get-ReconciliationFixture
        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings[0].PSObject.Properties.Remove('environment')
        $snapshot = @{ deployments = @(@{ commit = 'fbc68a9f0e02923880c8a06162a8d7cda2afac38'; workflowRun = 33457927112 }) }

        $result = Compare-DeliveryState -State $state -Snapshot $snapshot
        $derivedItem = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }

        $result.kind | Should Be 'STATE_RECONCILIATION_REQUIRED'
        $result.contradictions[0].kind | Should Be 'DEPLOYMENT_EVIDENCE_INCOMPLETE'
        $derivedItem.gates.devValidation.status | Should Be 'STALE'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
    }

    It 'fails closed when current cloud evidence is missing its workflow run even if the snapshot is missing it too' {
        $state = Get-ReconciliationFixture
        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings[0].PSObject.Properties.Remove('workflowRun')
        $snapshot = @{ deployments = @(@{ commit = 'fbc68a9f0e02923880c8a06162a8d7cda2afac38'; environment = 'development' }) }

        $result = Compare-DeliveryState -State $state -Snapshot $snapshot
        $derivedItem = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }

        $result.kind | Should Be 'STATE_RECONCILIATION_REQUIRED'
        $result.contradictions[0].kind | Should Be 'DEPLOYMENT_EVIDENCE_INCOMPLETE'
        $derivedItem.gates.devValidation.status | Should Be 'STALE'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
    }

    It 'fails closed and invalidates current cloud evidence when no Azure deployment snapshot is available' {
        $state = Get-ReconciliationFixture
        $result = Compare-DeliveryState -State $state -Snapshot @{ deployments = @() }
        $derivedItem = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }

        $result.kind | Should Be 'STATE_RECONCILIATION_REQUIRED'
        $result.contradictions[0].kind | Should Be 'AUTHORITATIVE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE'
        $derivedItem.gates.devValidation.status | Should Be 'STALE'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
    }

    It 'fails closed for current workflow evidence even when an environment is not recorded' {
        $state = Get-ReconciliationFixture
        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings[0].PSObject.Properties.Remove('environment')

        $result = Compare-DeliveryState -State $state -Snapshot @{ deployments = @() }
        $derivedItem = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }

        $result.kind | Should Be 'STATE_RECONCILIATION_REQUIRED'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
    }

    It 'invalidates only dependent evidence without mutating the input state' {
        $state = Get-ReconciliationFixture
        $result = Invalidate-DependentEvidence -State $state -WorkItemId '4A-E1' -Reason 'Azure deployment commit differs.'
        $derivedItem = $result.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $unrelatedItem = $result.currentSprint.workItems | Where-Object { $_.id -eq '4A-4' }

        $derivedItem.gates.devValidation.status | Should Be 'STALE'
        $derivedItem.evidenceBindings[0].status | Should Be 'STALE'
        $unrelatedItem.gates.devValidation.status | Should Be 'PENDING'
        ($state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }).gates.devValidation.status | Should Be 'PASS'
    }

    It 'refuses a schema-valid duplicate migration identity before a side effect can be invoked' {
        $state = Get-ReconciliationFixture
        $state.currentSprint.workItems[0].evidenceBindings = @(@{ commit = 'fbc68a9f0e02923880c8a06162a8d7cda2afac38'; workflowRun = 33457927112; status = 'CURRENT' })

        $failure = $null
        try { Assert-SideEffectNotDuplicate -Kind 'migration' -Identity 'migration:fbc68a9f0e02923880c8a06162a8d7cda2afac38:33457927112' -State $state } catch { $failure = $_.Exception.Message }

        $failure | Should Match 'already recorded'
    }

    It 'refuses the existing schema-valid E1 deployment identity before a side effect can be invoked' {
        $state = Get-ReconciliationFixture

        $failure = $null
        try { Assert-SideEffectNotDuplicate -Kind 'deployment' -Identity 'deployment:development:fbc68a9f0e02923880c8a06162a8d7cda2afac38:33457927112' -State $state } catch { $failure = $_.Exception.Message }

        $failure | Should Match 'already recorded'
    }

    It 'refuses a stale schema-valid deployment identity before a side effect can be invoked' {
        $state = Get-ReconciliationFixture
        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings[0].status = 'STALE'

        $failure = $null
        try { Assert-SideEffectNotDuplicate -Kind 'deployment' -Identity 'deployment:development:fbc68a9f0e02923880c8a06162a8d7cda2afac38:33457927112' -State $state } catch { $failure = $_.Exception.Message }

        $failure | Should Match 'already recorded'
    }

    It 'uses injected authoritative providers without requiring live GitHub or Azure access' {
        $snapshot = Get-AuthoritativeSnapshot -RepositoryRoot $repositoryRoot -GitSnapshotProvider {
            param($root)
            @{ head = 'fbc68a9f0e02923880c8a06162a8d7cda2afac38'; branch = 'development'; status = @(); worktrees = @() }
        } -GitHubSnapshotProvider { @{ pullRequests = @() } } -AzureSnapshotProvider {
            @{ deployments = @(); migrations = @() }
        }

        $snapshot.git.head | Should Be 'fbc68a9f0e02923880c8a06162a8d7cda2afac38'
        @($snapshot.github.pullRequests).Count | Should Be 0
        @($snapshot.deployments).Count | Should Be 0
        $snapshot.sideEffect | Should Be $false
    }

    It 'does not write files when reconciliation runs in WhatIf mode' {
        $paths = @(
            (Join-Path $repositoryRoot 'delivery/state.json'),
            (Join-Path $repositoryRoot 'delivery/evidence/pre-migration-baseline.json'),
            (Join-Path $repositoryRoot 'delivery/evidence/reconciliation.json')
        ) | Where-Object { Test-Path -LiteralPath $_ }
        $before = @{}
        foreach ($path in $paths) { $before[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }

        $powerShellHost = if ($env:OS -eq 'Windows_NT') { 'powershell' } else { 'pwsh' }
        $output = & $powerShellHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repositoryRoot 'ops/Invoke-SprintDelivery.ps1') -Reconcile -WhatIf

        $LASTEXITCODE | Should Be 0
        ($output -join "`n") | Should Match 'WHAT_IF'
        foreach ($path in $paths) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash | Should Be $before[$path] }
    }

    It 'uses an operating-system appropriate PowerShell host for child validation' {
        $testSource = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'ops/tests/SprintDelivery.Tests.ps1')

        $testSource | Should Match '(?s)\$powerShellHost\s*=\s*if\s*\(\$env:OS\s*-eq\s*''Windows_NT''\)\s*\{\s*''powershell''\s*\}\s*else\s*\{\s*''pwsh''\s*\}'
        $testSource | Should Match '&\s+\$powerShellHost\s+-NoProfile\s+-ExecutionPolicy\s+Bypass\s+-File'
    }
}

Describe 'Sprint delivery skill policy' -Tag 'skill-policy' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $skillNames = @(
            'sprint-orchestrator',
            'task-planning',
            'task-implementation',
            'automated-testing',
            'failure-resolution',
            'environment-validation',
            'qa'
        )
        $skillPaths = $skillNames | ForEach-Object {
            Join-Path $repositoryRoot ".agents/skills/$_/SKILL.md"
        }
    }

    It 'provides every focused delivery role with explicit inputs and outputs' {
        foreach ($skillPath in $skillPaths) {
            Test-Path -LiteralPath $skillPath | Should Be $true
            $content = Get-Content -Raw -LiteralPath $skillPath
            $content | Should Match '(?im)^## Inputs$'
            $content | Should Match '(?im)^## Outputs$'
            $content | Should Match 'Invoke-SprintDelivery\.ps1'
        }
    }

    It 'reserves lifecycle advancement for the sprint orchestrator' {
        foreach ($skillPath in $skillPaths) {
            $content = Get-Content -Raw -LiteralPath $skillPath
            $isOrchestrator = $skillPath -match 'sprint-orchestrator'

            if ($isOrchestrator) {
                $content | Should Match '(?i)advance lifecycle state'
            }
            else {
                $content | Should Match '(?i)must not advance lifecycle state'
            }
        }
    }

    It 'keeps skills free of environment-specific identifiers, model names, and secrets' {
        $disallowedPatterns = @(
            '(?i)tenant\s*id',
            '(?i)subscription\s*id',
            '(?i)(api[_ -]?key|client[_ -]?secret|connection\s*string|password|token)\s*[:=]',
            '(?i)gpt-[0-9]',
            '(?i)claude-[0-9]',
            '(?i)environment\s*(id|name|value)\s*[:=]'
        )

        foreach ($skillPath in $skillPaths) {
            $content = Get-Content -Raw -LiteralPath $skillPath
            foreach ($pattern in $disallowedPatterns) {
                $content | Should Not Match $pattern
            }
        }
    }

    It 'links the repository guide to the runbook without embedding its workflow details' {
        $agentsPath = Join-Path $repositoryRoot 'AGENTS.md'
        $agents = Get-Content -Raw -LiteralPath $agentsPath

        $agents | Should Match 'docs/operations/sprint-delivery-workflow\.md'
        $agents | Should Not Match 'Fresh-session resume'
        $agents | Should Not Match 'CUTOVER_BLOCKED'
    }
}

Describe 'Sprint delivery CI policy' -Tag 'ci-policy' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $workflowPath = Join-Path $repositoryRoot '.github/workflows/sprint-delivery-validation.yml'
        $templatePath = Join-Path $repositoryRoot '.github/pull_request_template.md'
    }

    It 'uses a pull-request-only workflow for every delivery workflow input' {
        Test-Path -LiteralPath $workflowPath | Should Be $true
        $workflow = Get-Content -Raw -LiteralPath $workflowPath

        $workflow | Should Match '(?m)^on:\s*$'
        $workflow | Should Match '(?m)^\s{2}pull_request:\s*$'
        $workflow | Should Match "(?m)^\s{6}- 'delivery/\*\*'"
        $workflow | Should Match "(?m)^\s{6}- 'ops/\*\*'"
        $workflow | Should Match "(?m)^\s{6}- '\.agents/skills/\*\*'"
        $workflow | Should Match "(?m)^\s{6}- 'AGENTS\.md'"
        $workflow | Should Match "(?m)^\s{6}- '\.github/workflows/\*\*'"
        $workflow | Should Not Match '(?m)^\s{2}(push|workflow_dispatch|schedule):'
    }

    It 'uses pinned actions and a read-only delivery test command' {
        $workflow = Get-Content -Raw -LiteralPath $workflowPath

        $workflow | Should Match '(?m)^permissions:\s*$'
        $workflow | Should Match '(?m)^\s{2}contents:\s+read\s*$'
        $workflow | Should Not Match '(?m)^\s{2}(actions|checks|contents|deployments|id-token|issues|packages|pull-requests|statuses):\s+(write|read-all|write-all)\s*$'
        $workflow | Should Match 'uses:\s+actions/checkout@[0-9a-f]{40}'
        $workflow | Should Not Match 'uses:\s+[^\s@]+@(?:v?\d|main|master|latest)'
        $workflow | Should Match 'Install-Module Pester'
        $workflow | Should Match 'pwsh\s+-NoProfile\s+-ExecutionPolicy\s+Bypass\s+-File\s+ops/Test-SprintDelivery\.ps1'
    }

    It 'does not contain cloud, repository, environment, or evidence mutation commands' {
        $workflow = Get-Content -Raw -LiteralPath $workflowPath
        $prohibitedPatterns = @(
            '(?i)azure/login',
            '(?i)az\s+(login|deployment|group\s+create|containerapp|sql)',
            '(?i)\b(deploy|provision|migrat(?:e|ion))\b',
            '(?i)\bgh\s+(api|pr|workflow|run)',
            '(?i)git\s+(push|merge|commit|checkout)',
            '(?i)(upload-artifact|download-artifact)',
            '(?i)(Set-Content|Add-Content|Out-File|New-Item|Copy-Item|Move-Item|Remove-Item)'
        )

        foreach ($pattern in $prohibitedPatterns) {
            $workflow | Should Not Match $pattern
        }
    }

    It 'asks pull requests to disclose delivery evidence and gate status without owning lifecycle state' {
        Test-Path -LiteralPath $templatePath | Should Be $true
        $template = Get-Content -Raw -LiteralPath $templatePath

        $template | Should Match '(?i)delivery state'
        $template | Should Match '(?i)evidence'
        $template | Should Match '(?i)gate status'
        $template | Should Match '(?i)does not advance lifecycle state'
    }
}

Describe 'Sprint delivery migration cutover' -Tag 'migration' {
    BeforeAll {
        $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        . (Join-Path $repositoryRoot 'ops/Invoke-SprintDelivery.ps1')
        $config = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/config.json')

        function Get-CutoverFixture {
            return Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
        }

        function Get-CutoverInputs {
            return @{
                WorktreeSnapshot = @{
                    isPreserved = $true
                    sourceWorktree = 'C:/repos/OrderApp/.worktrees/feature-sprint4-identity-design'
                    featureBranch = 'feature/sprint4-identity-design'
                    productHead = '5c0e1ab136f477127ce194426742a27d704d20d4'
                }
                Baselines = @{ preMigration = $true; postMigration = $true }
                SelfTestsPassed = $true
                SelfTestEvidenceAvailable = $true
                Reconciliation = @{ kind = 'STATE_RECONCILIATION_AGREES'; contradictions = @() }
            }
        }
    }

    It 'allows cutover only when every required read-only proof is supplied' {
        $inputs = Get-CutoverInputs
        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $result.status | Should Be 'WORKFLOW_CUTOVER_COMPLETE'
        $result.blockers.Count | Should Be 0
        $result.sideEffect | Should Be $false
    }

    It 'blocks cutover when a reconciliation contradiction remains' {
        $inputs = Get-CutoverInputs
        $inputs.Reconciliation = @{ kind = 'STATE_RECONCILIATION_REQUIRED'; contradictions = @(@{ kind = 'AUTHORITATIVE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE' }) }

        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $result.status | Should Be 'CUTOVER_BLOCKED'
        ($result.blockers -join "`n") | Should Match 'AUTHORITATIVE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE'
    }

    It 'blocks cutover when the Azure deployment authority was unavailable despite an otherwise agreeing snapshot' {
        $inputs = Get-CutoverInputs
        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs -AuthoritativeDeploymentEvidenceAvailable $false

        $result.status | Should Be 'CUTOVER_BLOCKED'
        ($result.blockers -join "`n") | Should Match 'AUTHORITATIVE_AZURE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE'
    }

    It 'blocks cutover when developer evidence passes but CI subsequently fails' {
        $inputs = Get-CutoverInputs
        $inputs.Baselines = @{ preMigration = $true; postMigration = $true; ci = 'FAIL' }

        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $result.status | Should Be 'CUTOVER_BLOCKED'
        ($result.blockers -join "`n") | Should Match 'CI status is FAIL'
    }

    It 'reports unavailable self-test proof without claiming a passing test failed' {
        $inputs = Get-CutoverInputs
        $inputs.SelfTestsPassed = $true
        $inputs.SelfTestEvidenceAvailable = $false

        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $result.status | Should Be 'CUTOVER_BLOCKED'
        ($result.blockers -join "`n") | Should Match 'SELF_TEST_EVIDENCE_UNAVAILABLE'
        ($result.blockers -join "`n") | Should Not Match 'self-tests did not pass'
    }

    It 'blocks cutover if the preserved product worktree snapshot is incomplete' {
        $inputs = Get-CutoverInputs
        $inputs.WorktreeSnapshot.isPreserved = $false

        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $result.status | Should Be 'CUTOVER_BLOCKED'
        ($result.blockers -join "`n") | Should Match 'product worktree'
    }

    It 'resumes from committed preserved-worktree evidence when a fresh clone has no local product worktree' {
        $evidence = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/evidence/cutover-validation.json')

        $snapshot = Resolve-PreservedProductWorktreeSnapshot -RepositoryRoot $repositoryRoot -CutoverEvidence $evidence -WorktreeProvider { @() }
        $inputs = Get-CutoverInputs
        $inputs.WorktreeSnapshot = $snapshot

        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $snapshot.isPreserved | Should Be $true
        $snapshot.localWorktreePresent | Should Be $false
        $snapshot.featureBranch | Should Be 'feature/sprint4-identity-design'
        $snapshot.productHead | Should Be '5c0e1ab136f477127ce194426742a27d704d20d4'
        $result.status | Should Be 'WORKFLOW_CUTOVER_COMPLETE'
    }

    It 'blocks cutover when a locally present preserved worktree head differs from committed evidence' {
        $evidence = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/evidence/cutover-validation.json')
        $records = @([pscustomobject]@{
            path = 'C:/repos/OrderApp/.worktrees/feature-sprint4-identity-design'
            branch = 'feature/sprint4-identity-design'
            head = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        })

        $snapshot = Resolve-PreservedProductWorktreeSnapshot -RepositoryRoot $repositoryRoot -CutoverEvidence $evidence -WorktreeProvider { $records }
        $inputs = Get-CutoverInputs
        $inputs.WorktreeSnapshot = $snapshot

        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $snapshot.isPreserved | Should Be $false
        $snapshot.validationError | Should Be 'PRESERVED_PRODUCT_WORKTREE_HEAD_MISMATCH'
        $result.status | Should Be 'CUTOVER_BLOCKED'
        ($result.blockers -join "`n") | Should Match 'product worktree'
    }

    It 'keeps D1 as a human decision without escalating it to a model action' {
        $inputs = Get-CutoverInputs
        $result = Set-WorkflowCutover -State (Get-CutoverFixture) -Config $config @inputs

        $d1 = $result.state.currentSprint.workItems | Where-Object { $_.id -eq '4A-7-D1' }
        $d1.blockers[0].status | Should Be 'HUMAN_DECISION_REQUIRED'
        ($result.blockers -join "`n") | Should Not Match 'D1'
    }

    It 'rejects an incompatible state version before evaluating cutover' {
        $state = Get-CutoverFixture
        $state.stateSchemaVersion = '99.0'
        $inputs = Get-CutoverInputs

        { Set-WorkflowCutover -State $state -Config $config @inputs } | Should Throw
    }

    It 'recognizes one lifecycle owner and rejects a competing owner' {
        $state = Get-CutoverFixture
        (Test-WorkflowLifecycleOwner -State $state -ExpectedOwner 'sprint-orchestrator') | Should Be $true

        $state.workflowLifecycleOwner = 'another-orchestrator'
        { Test-WorkflowLifecycleOwner -State $state -ExpectedOwner 'sprint-orchestrator' } | Should Throw
    }

    It 'rejects a state that declares a completed cutover with unresolved blockers' {
        $state = Get-CutoverFixture
        $state.cutover.status = 'WORKFLOW_CUTOVER_COMPLETE'
        $state.cutover.blockers = @('AUTHORITATIVE_AZURE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE')

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw
    }

    It 'rejects a null cutover blockers value while accepting an empty collection' {
        $state = Get-CutoverFixture
        $state.cutover.blockers = $null

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw

        $state.cutover.status = 'WORKFLOW_CUTOVER_COMPLETE'
        $state.cutover.blockers = [System.Collections.ArrayList]::new()
        { Test-SprintDeliveryState -State $state -Config $config } | Should Not Throw
    }

    It 'does not resume product work while the workflow cutover remains blocked' {
        $state = Get-CutoverFixture

        $action = Get-NextDeliveryAction -State $state -Config $config

        $action.kind | Should Be 'CUTOVER_BLOCKED'
        $action.workItemId | Should Be $null
    }

    It 'requires an immutable deployment artifact for current deployment evidence' {
        $state = Get-CutoverFixture
        $e1 = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-E1' }
        $e1.evidenceBindings[0].status = 'CURRENT'

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw
    }

    It 'rejects released work without a current immutable evidence binding' {
        $state = Get-CutoverFixture
        $item = $state.currentSprint.workItems | Where-Object { $_.id -eq '4A-4' }
        $item.lifecycle = 'RELEASED'
        $item.stage = 'QA'
        $item.gates.codeReview.status = 'PASS'
        $item.gates.ci.status = 'PASS'
        $item.gates.devValidation.status = 'PASS'
        $item.gates.independentReview.status = 'PASS'
        $item.gates.qaValidation.status = 'PASS'

        { Test-SprintDeliveryState -State $state -Config $config } | Should Throw
    }

    It 'accepts a caller-supplied read-only reconciliation snapshot without cloud authentication' {
        $powerShellHost = if ($env:OS -eq 'Windows_NT') { 'powershell' } else { 'pwsh' }
        $snapshotPath = Join-Path $repositoryRoot 'ops/tests/fixtures/read-only-reconciliation-snapshot.json'

        $output = & $powerShellHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repositoryRoot 'ops/Invoke-SprintDelivery.ps1') -Reconcile -WhatIf -ReconciliationSnapshotPath $snapshotPath

        $LASTEXITCODE | Should Be 0
        ($output -join "`n") | Should Match 'CUTOVER_BLOCKED'
    }
}
