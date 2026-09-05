Describe 'Sprint delivery contracts' -Tag 'contracts' {
BeforeAll {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $configPath = Join-Path $repositoryRoot 'delivery/config.json'
    $statePath = Join-Path $repositoryRoot 'delivery/state.json'

    $config = Get-Content -Raw $configPath | ConvertFrom-Json

    function Get-DeliveryFixture {
        param([Parameter(Mandatory)][string] $Name)

        $fixture = Get-Content -Raw $statePath | ConvertFrom-Json
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
            'imported-sprint-4a' { }
            default { throw "Unknown delivery fixture '$Name'." }
        }

        return $fixture
    }

    function Test-SprintDeliveryState {
        param(
            [Parameter(Mandatory)] $State,
            [Parameter(Mandatory)] $Config
        )

        foreach ($field in 'workflowVersion', 'stateSchemaVersion', 'configurationVersion', 'currentSprint') {
            if ($field -notin $State.PSObject.Properties.Name) { throw "State is missing '$field'." }
        }

        if ($State.workflowVersion -ne $Config.workflowVersion -or
            $State.configurationVersion -ne $Config.configurationVersion) {
            throw 'State version does not match configuration version.'
        }

        foreach ($workItem in $State.currentSprint.workItems) {
            if ($workItem.lifecycle -notin $Config.vocabulary.lifecycle) {
                throw "Unknown lifecycle '$($workItem.lifecycle)'."
            }

            foreach ($gateName in $workItem.gates.PSObject.Properties.Name) {
                $gate = $workItem.gates.$gateName
                if ($gate.status -notin $Config.vocabulary.gateStatus) {
                    throw "Unknown gate status '$($gate.status)'."
                }
            }

            if ($workItem.lifecycle -in @('MERGED', 'DEV_DEPLOYED', 'QA_DEPLOYED', 'RELEASED') -and
                $workItem.gates.ci.status -eq 'FAIL') {
                throw "Lifecycle '$($workItem.lifecycle)' conflicts with failed ci gate."
            }
        }

        return $true
    }

    function Assert-TaskDone {
        param(
            [Parameter(Mandatory)] $WorkItem,
            [Parameter(Mandatory)] $Config
        )

        foreach ($gateName in $Config.requiredGatesByRisk.($WorkItem.risk)) {
            $status = $WorkItem.gates.$gateName.status
            if ($status -notin @('PASS', 'NOT_APPLICABLE')) {
                throw "Required gate '$gateName' is '$status'."
            }
        }

        return $true
    }
}

    It 'rejects a state with a lifecycle/gate collision' {
        $state = Get-DeliveryFixture 'merged-with-failed-ci'
        $errorRecord = $null

        try { Test-SprintDeliveryState -State $state -Config $config } catch { $errorRecord = $_ }

        $errorRecord | Should Not BeNullOrEmpty
        $errorRecord.Exception.Message | Should Match 'Lifecycle.*gate'
    }

    It 'rejects completion while a required gate is pending' {
        $state = Get-DeliveryFixture 'task-done-with-pending-review'
        $errorRecord = $null

        try { Assert-TaskDone -WorkItem $state.currentSprint.workItems[0] -Config $config } catch { $errorRecord = $_ }

        $errorRecord | Should Not BeNullOrEmpty
        $errorRecord.Exception.Message | Should Match 'codeReview'
    }

    It 'accepts the imported Sprint 4A state' {
        $state = Get-DeliveryFixture 'imported-sprint-4a'

        (Test-SprintDeliveryState -State $state -Config $config) | Should Be $true
    }
}
