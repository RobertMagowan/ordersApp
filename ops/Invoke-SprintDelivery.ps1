[CmdletBinding()]
param(
    [switch] $WhatIf
)

Set-StrictMode -Version Latest

function Get-DeliveryMemberValue {
    param(
        [Parameter(Mandatory)] $InputObject,
        [Parameter(Mandatory)][string] $Name
    )

    if ($InputObject -is [System.Collections.IDictionary]) {
        return $InputObject[$Name]
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-DeliveryMemberExists {
    param(
        [Parameter(Mandatory)] $InputObject,
        [Parameter(Mandatory)][string] $Name
    )

    if ($InputObject -is [System.Collections.IDictionary]) {
        return $InputObject.Contains($Name)
    }

    return $null -ne $InputObject.PSObject.Properties[$Name]
}

function Test-DeliveryIntegerInRange {
    param(
        [Parameter(Mandatory)] $Value,
        [Parameter(Mandatory)][long] $Minimum,
        [Parameter(Mandatory)][long] $Maximum
    )

    if ($Value -isnot [ValueType] -or [double]$Value -ne [math]::Floor([double]$Value) -or $Value -lt $Minimum -or $Value -gt $Maximum) {
        return $false
    }

    return $true
}

function Read-DeliveryJson {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Delivery JSON file was not found: $Path"
    }

    $json = Get-Content -LiteralPath $Path -Raw
    $convertFromJson = Get-Command ConvertFrom-Json
    if ($convertFromJson.Parameters.ContainsKey('AsHashtable')) {
        return $json | ConvertFrom-Json -AsHashtable
    }

    return $json | ConvertFrom-Json
}

function Test-SprintDeliveryState {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Config
    )

    foreach ($name in 'workflowVersion', 'stateSchemaVersion', 'configurationVersion', 'currentSprint') {
        if (-not (Test-DeliveryMemberExists -InputObject $State -Name $name)) {
            throw "State is missing '$name'."
        }
    }

    if ((Get-DeliveryMemberValue -InputObject $State -Name 'workflowVersion') -ne (Get-DeliveryMemberValue -InputObject $Config -Name 'workflowVersion')) {
        throw 'State workflowVersion does not match configuration.'
    }

    if ((Get-DeliveryMemberValue -InputObject $State -Name 'stateSchemaVersion') -ne (Get-DeliveryMemberValue -InputObject $Config -Name 'workflowVersion')) {
        throw 'State stateSchemaVersion is not supported by this workflow.'
    }

    if ((Get-DeliveryMemberValue -InputObject $State -Name 'configurationVersion') -ne (Get-DeliveryMemberValue -InputObject $Config -Name 'configurationVersion')) {
        throw 'State configurationVersion does not match configuration.'
    }

    $vocabulary = Get-DeliveryMemberValue -InputObject $Config -Name 'vocabulary'
    $currentSprint = Get-DeliveryMemberValue -InputObject $State -Name 'currentSprint'
    $workItems = Get-DeliveryMemberValue -InputObject $currentSprint -Name 'workItems'
    if ($null -eq $workItems) {
        throw 'State currentSprint is missing workItems.'
    }

    foreach ($workItem in @($workItems)) {
        foreach ($name in 'id', 'risk', 'lifecycle', 'stage', 'evidenceBindings', 'retryCounters', 'gates', 'blockers') {
            if (-not (Test-DeliveryMemberExists -InputObject $workItem -Name $name)) {
                throw "Work item is missing '$name'."
            }
        }

        $lifecycle = Get-DeliveryMemberValue -InputObject $workItem -Name 'lifecycle'
        $stage = Get-DeliveryMemberValue -InputObject $workItem -Name 'stage'
        if ($lifecycle -notin @(Get-DeliveryMemberValue -InputObject $vocabulary -Name 'lifecycle')) {
            throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid lifecycle '$lifecycle'."
        }
        if ($stage -notin @(Get-DeliveryMemberValue -InputObject $vocabulary -Name 'stage')) {
            throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid stage '$stage'."
        }

        $risk = Get-DeliveryMemberValue -InputObject $workItem -Name 'risk'
        $requiredGateNames = Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $Config -Name 'requiredGatesByRisk') -Name $risk
        if ($null -eq $requiredGateNames) {
            throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has unknown risk '$risk'."
        }

        $gates = Get-DeliveryMemberValue -InputObject $workItem -Name 'gates'
        foreach ($gateName in @($requiredGateNames)) {
            $gate = Get-DeliveryMemberValue -InputObject $gates -Name $gateName
            if ($null -eq $gate) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' is missing required gate '$gateName'."
            }

            $status = Get-DeliveryMemberValue -InputObject $gate -Name 'status'
            if ($status -notin @(Get-DeliveryMemberValue -InputObject $vocabulary -Name 'gateStatus')) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid gate status '$status'."
            }
        }

        $evidenceBindings = Get-DeliveryMemberValue -InputObject $workItem -Name 'evidenceBindings'
        if ($evidenceBindings -is [string]) {
            throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid evidenceBindings."
        }
        foreach ($binding in @($evidenceBindings)) {
            if ($null -eq $binding -or -not (Test-DeliveryMemberExists -InputObject $binding -Name 'status')) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has an invalid evidence binding."
            }

            $bindingStatus = Get-DeliveryMemberValue -InputObject $binding -Name 'status'
            if ($bindingStatus -notin @('CURRENT', 'STALE', 'HISTORICAL_UNVERIFIED', 'SUPERSEDED')) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid evidence status '$bindingStatus'."
            }

            $commit = Get-DeliveryMemberValue -InputObject $binding -Name 'commit'
            if ($null -ne $commit -and $commit -notmatch '^[0-9a-f]{7,40}$') {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid evidence commit '$commit'."
            }

            $workflowRun = Get-DeliveryMemberValue -InputObject $binding -Name 'workflowRun'
            if ($null -ne $workflowRun -and -not (Test-DeliveryIntegerInRange -Value $workflowRun -Minimum 1 -Maximum ([long]::MaxValue))) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid evidence workflowRun '$workflowRun'."
            }

            $environment = Get-DeliveryMemberValue -InputObject $binding -Name 'environment'
            if ($null -ne $environment -and $environment -notin @('development', 'test', 'production')) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid evidence environment '$environment'."
            }
        }

        $retryCounters = Get-DeliveryMemberValue -InputObject $workItem -Name 'retryCounters'
        foreach ($counterName in 'command', 'deployment') {
            $counter = Get-DeliveryMemberValue -InputObject $retryCounters -Name $counterName
            $limit = Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $Config -Name 'retryLimits') -Name $counterName
            if ($null -eq $counter -or -not (Test-DeliveryIntegerInRange -Value $counter -Minimum 0 -Maximum $limit)) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid $counterName retry counter."
            }
        }

        $blockers = Get-DeliveryMemberValue -InputObject $workItem -Name 'blockers'
        if ($blockers -is [string]) {
            throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid blockers."
        }
        foreach ($blocker in @($blockers)) {
            if ($null -eq $blocker -or -not (Test-DeliveryMemberExists -InputObject $blocker -Name 'status') -or -not (Test-DeliveryMemberExists -InputObject $blocker -Name 'reason')) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid blocker shape."
            }

            $blockerStatus = Get-DeliveryMemberValue -InputObject $blocker -Name 'status'
            $blockerReason = Get-DeliveryMemberValue -InputObject $blocker -Name 'reason'
            if ($blockerStatus -notin @(Get-DeliveryMemberValue -InputObject $vocabulary -Name 'blockerStatus') -or [string]::IsNullOrWhiteSpace($blockerReason)) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has invalid blocker status or reason."
            }
        }

        $ciStatus = Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $gates -Name 'ci') -Name 'status'
        if ($lifecycle -in @('MERGED', 'DEV_DEPLOYED', 'QA_DEPLOYED', 'RELEASED') -and $ciStatus -eq 'FAIL') {
            throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' lifecycle conflicts with failed ci gate."
        }
    }

    return $true
}

function Assert-TaskDone {
    param(
        [Parameter(Mandatory)] $WorkItem,
        [Parameter(Mandatory)] $Config
    )

    $risk = Get-DeliveryMemberValue -InputObject $WorkItem -Name 'risk'
    $requiredGateNames = Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $Config -Name 'requiredGatesByRisk') -Name $risk
    if ($null -eq $requiredGateNames) {
        throw "Work item has unknown risk '$risk'."
    }

    $gates = Get-DeliveryMemberValue -InputObject $WorkItem -Name 'gates'
    foreach ($gateName in @($requiredGateNames)) {
        $gate = Get-DeliveryMemberValue -InputObject $gates -Name $gateName
        $status = if ($null -eq $gate) { 'MISSING' } else { Get-DeliveryMemberValue -InputObject $gate -Name 'status' }
        if ($status -notin @('PASS', 'NOT_APPLICABLE')) {
            throw "Required gate '$gateName' is '$status'; task completion cannot be derived."
        }
    }

    return $true
}

function Get-NextDeliveryAction {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Config
    )

    Test-SprintDeliveryState -State $State -Config $Config | Out-Null
    $workItems = @(Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $State -Name 'currentSprint') -Name 'workItems')

    $nextItem = $workItems | Where-Object { (Get-DeliveryMemberValue -InputObject $_ -Name 'lifecycle') -in @('TODO', 'IN_PROGRESS', 'PR_OPEN') } | Select-Object -First 1
    if ($null -eq $nextItem) {
        return [pscustomobject]@{
            kind = 'NO_ACTION'
            workItemId = $null
            reason = 'No actionable work item is recorded in the current sprint.'
            sideEffect = $false
        }
    }

    foreach ($blocker in @(Get-DeliveryMemberValue -InputObject $nextItem -Name 'blockers')) {
        if ((Get-DeliveryMemberValue -InputObject $blocker -Name 'status') -eq (Get-DeliveryMemberValue -InputObject $Config -Name 'humanDecisionStatus')) {
            return [pscustomobject]@{
                kind = 'HUMAN_DECISION_REQUIRED'
                workItemId = Get-DeliveryMemberValue -InputObject $nextItem -Name 'id'
                reason = Get-DeliveryMemberValue -InputObject $blocker -Name 'reason'
                sideEffect = $false
            }
        }
    }

    return [pscustomobject]@{
        kind = 'WORK_ITEM_READY'
        workItemId = Get-DeliveryMemberValue -InputObject $nextItem -Name 'id'
        reason = "Continue $((Get-DeliveryMemberValue -InputObject $nextItem -Name 'stage'))."
        sideEffect = $false
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $config = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/config.json')
    $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
    Test-SprintDeliveryState -State $state -Config $config | Out-Null
    $action = Get-NextDeliveryAction -State $state -Config $config

    [pscustomobject]@{
        mode = if ($WhatIf) { 'WHAT_IF' } else { 'READ_ONLY' }
        sprint = (Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $state -Name 'currentSprint') -Name 'id')
        action = $action
    } | ConvertTo-Json -Depth 8
}
