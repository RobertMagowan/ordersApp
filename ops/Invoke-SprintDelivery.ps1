[CmdletBinding()]
param(
    [switch] $WhatIf,
    [switch] $Reconcile,
    [string] $ReconciliationSnapshotPath,
    [string] $CutoverEvidencePath
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

function Copy-DeliveryObject {
    param([Parameter(Mandatory)] $InputObject)

    $json = $InputObject | ConvertTo-Json -Depth 64
    $convertFromJson = Get-Command ConvertFrom-Json
    if ($convertFromJson.Parameters.ContainsKey('AsHashtable')) {
        return $json | ConvertFrom-Json -AsHashtable
    }

    return $json | ConvertFrom-Json
}

function Get-AuthoritativeSnapshot {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [scriptblock] $GitSnapshotProvider,
        [scriptblock] $GitHubSnapshotProvider,
        [scriptblock] $AzureSnapshotProvider
    )

    $git = if ($null -ne $GitSnapshotProvider) {
        & $GitSnapshotProvider $RepositoryRoot
    }
    else {
        [pscustomobject]@{
            head = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
            branch = (& git -C $RepositoryRoot branch --show-current).Trim()
            status = @(& git -C $RepositoryRoot status --porcelain)
            worktrees = @(& git -C $RepositoryRoot worktree list --porcelain)
        }
    }

    # Cloud evidence is deliberately provider-injected. This command never authenticates
    # to or mutates GitHub/Azure; callers may supply an explicitly read-only provider.
    $github = if ($null -ne $GitHubSnapshotProvider) { & $GitHubSnapshotProvider } else { $null }
    $azure = if ($null -ne $AzureSnapshotProvider) { & $AzureSnapshotProvider } else { $null }

    return [pscustomobject]@{
        git = $git
        github = $github
        azure = $azure
        deployments = if ($null -ne $azure -and $null -ne (Get-DeliveryMemberValue -InputObject $azure -Name 'deployments')) { @(Get-DeliveryMemberValue -InputObject $azure -Name 'deployments') } else { @() }
        migrations = if ($null -ne $azure -and $null -ne (Get-DeliveryMemberValue -InputObject $azure -Name 'migrations')) { @(Get-DeliveryMemberValue -InputObject $azure -Name 'migrations') } else { @() }
        sideEffect = $false
    }
}

function Invalidate-DependentEvidence {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)][string] $WorkItemId,
        [Parameter(Mandatory)][string] $Reason
    )

    $derivedState = Copy-DeliveryObject -InputObject $State
    $workItem = @(Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $derivedState -Name 'currentSprint') -Name 'workItems') |
        Where-Object { (Get-DeliveryMemberValue -InputObject $_ -Name 'id') -eq $WorkItemId } |
        Select-Object -First 1
    if ($null -eq $workItem) {
        throw "Cannot invalidate evidence for unknown work item '$WorkItemId'."
    }

    foreach ($binding in @(Get-DeliveryMemberValue -InputObject $workItem -Name 'evidenceBindings')) {
        $binding.status = 'STALE'
    }

    $gates = Get-DeliveryMemberValue -InputObject $workItem -Name 'gates'
    $devValidation = Get-DeliveryMemberValue -InputObject $gates -Name 'devValidation'
    if ($null -ne $devValidation) {
        $devValidation.status = 'STALE'
    }

    return $derivedState
}

function Get-CanonicalSideEffectIdentity {
    param(
        [Parameter(Mandatory)][ValidateSet('deployment', 'migration')][string] $Kind,
        [Parameter(Mandatory)] $Binding
    )

    $commit = Get-DeliveryMemberValue -InputObject $Binding -Name 'commit'
    $workflowRun = Get-DeliveryMemberValue -InputObject $Binding -Name 'workflowRun'
    if ([string]::IsNullOrWhiteSpace($commit) -or $commit -notmatch '^[0-9a-f]{7,40}$' -or
        $null -eq $workflowRun -or -not (Test-DeliveryIntegerInRange -Value $workflowRun -Minimum 1 -Maximum ([long]::MaxValue))) {
        return $null
    }

    if ($Kind -eq 'migration') {
        return "migration:${commit}:$workflowRun"
    }

    $environment = Get-DeliveryMemberValue -InputObject $Binding -Name 'environment'
    if ($environment -notin @('development', 'test', 'production')) {
        return $null
    }

    return "deployment:${environment}:${commit}:$workflowRun"
}

function Compare-DeliveryState {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Snapshot
    )

    $derivedState = Copy-DeliveryObject -InputObject $State
    $contradictions = @()
    $deploymentValue = Get-DeliveryMemberValue -InputObject $Snapshot -Name 'deployments'
    $deployments = if ($null -eq $deploymentValue) { @() } else { @($deploymentValue) }

    foreach ($workItem in @(Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $State -Name 'currentSprint') -Name 'workItems')) {
        $workItemId = Get-DeliveryMemberValue -InputObject $workItem -Name 'id'
        foreach ($binding in @(Get-DeliveryMemberValue -InputObject $workItem -Name 'evidenceBindings')) {
            if ((Get-DeliveryMemberValue -InputObject $binding -Name 'status') -ne 'CURRENT') {
                continue
            }

            $environment = Get-DeliveryMemberValue -InputObject $binding -Name 'environment'
            $workflowRun = Get-DeliveryMemberValue -InputObject $binding -Name 'workflowRun'
            if ($null -eq $environment -and $null -eq $workflowRun) {
                continue
            }

            $commit = Get-DeliveryMemberValue -InputObject $binding -Name 'commit'
            if ($null -eq (Get-CanonicalSideEffectIdentity -Kind 'deployment' -Binding $binding)) {
                $derivedState = Invalidate-DependentEvidence -State $derivedState -WorkItemId $workItemId -Reason 'Current deployment evidence is missing a canonical immutable identifier.'
                $contradictions += [pscustomobject]@{
                    workItemId = $workItemId
                    kind = 'DEPLOYMENT_EVIDENCE_INCOMPLETE'
                    expected = [pscustomobject]@{
                        required = @('commit', 'environment', 'workflowRun')
                    }
                    actual = [pscustomobject]@{
                        commit = $commit
                        workflowRun = $workflowRun
                        environment = $environment
                    }
                }
                continue
            }

            $sameEnvironment = @($deployments | Where-Object {
                $null -ne $environment -and (Get-DeliveryMemberValue -InputObject $_ -Name 'environment') -eq $environment
            })
            if ($sameEnvironment.Count -eq 0) {
                $derivedState = Invalidate-DependentEvidence -State $derivedState -WorkItemId $workItemId -Reason 'No authoritative deployment snapshot was supplied for current cloud evidence.'
                $contradictions += [pscustomobject]@{
                    workItemId = $workItemId
                    kind = 'AUTHORITATIVE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE'
                    expected = [pscustomobject]@{
                        commit = Get-DeliveryMemberValue -InputObject $binding -Name 'commit'
                        workflowRun = Get-DeliveryMemberValue -InputObject $binding -Name 'workflowRun'
                        environment = Get-DeliveryMemberValue -InputObject $binding -Name 'environment'
                        artifact = Get-DeliveryMemberValue -InputObject $binding -Name 'artifact'
                    }
                    actual = @()
                }
                continue
            }

            $exactMatch = @($sameEnvironment | Where-Object {
                (Get-DeliveryMemberValue -InputObject $_ -Name 'commit') -eq (Get-DeliveryMemberValue -InputObject $binding -Name 'commit') -and
                (Get-DeliveryMemberValue -InputObject $_ -Name 'workflowRun') -eq (Get-DeliveryMemberValue -InputObject $binding -Name 'workflowRun') -and
                (Get-DeliveryMemberValue -InputObject $_ -Name 'environment') -eq (Get-DeliveryMemberValue -InputObject $binding -Name 'environment') -and
                (Get-DeliveryMemberValue -InputObject $_ -Name 'artifact') -eq (Get-DeliveryMemberValue -InputObject $binding -Name 'artifact')
            })
            if ($exactMatch.Count -gt 0) {
                continue
            }

            $derivedState = Invalidate-DependentEvidence -State $derivedState -WorkItemId $workItemId -Reason 'Immutable deployment identifiers differ from the authoritative snapshot.'
            $contradictions += [pscustomobject]@{
                workItemId = $workItemId
                kind = 'DEPLOYMENT_EVIDENCE_MISMATCH'
                expected = [pscustomobject]@{
                    commit = Get-DeliveryMemberValue -InputObject $binding -Name 'commit'
                    workflowRun = Get-DeliveryMemberValue -InputObject $binding -Name 'workflowRun'
                    environment = Get-DeliveryMemberValue -InputObject $binding -Name 'environment'
                    artifact = Get-DeliveryMemberValue -InputObject $binding -Name 'artifact'
                }
                actual = $sameEnvironment
            }
        }
    }

    return [pscustomobject]@{
        kind = if ($contradictions.Count -gt 0) { 'STATE_RECONCILIATION_REQUIRED' } else { 'STATE_RECONCILIATION_AGREES' }
        state = $derivedState
        contradictions = $contradictions
        sideEffect = $false
    }
}

function Assert-SideEffectNotDuplicate {
    param(
        [Parameter(Mandatory)][ValidateSet('deployment', 'migration')][string] $Kind,
        [Parameter(Mandatory)][string] $Identity,
        [Parameter(Mandatory)] $State
    )

    foreach ($workItem in @(Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $State -Name 'currentSprint') -Name 'workItems')) {
        foreach ($binding in @(Get-DeliveryMemberValue -InputObject $workItem -Name 'evidenceBindings')) {
            $recordedIdentity = Get-CanonicalSideEffectIdentity -Kind $Kind -Binding $binding
            if ($null -ne $recordedIdentity -and $recordedIdentity -ceq $Identity) {
                throw "Side effect '$Kind' with identity '$Identity' is already recorded and cannot be invoked."
            }
        }
    }

    return $true
}

function Test-SprintDeliveryState {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Config
    )

    foreach ($name in 'workflowVersion', 'stateSchemaVersion', 'configurationVersion', 'workflowLifecycleOwner', 'cutover', 'sprintPlanSource', 'sprintPlanRevision', 'currentSprint') {
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

    Test-WorkflowLifecycleOwner -State $State -ExpectedOwner 'sprint-orchestrator' | Out-Null
    $cutover = Get-DeliveryMemberValue -InputObject $State -Name 'cutover'
    $cutoverStatus = Get-DeliveryMemberValue -InputObject $cutover -Name 'status'
    $cutoverBlockers = $null
    if ($cutover -is [System.Collections.IDictionary]) {
        if ($cutover.Contains('blockers')) {
            $cutoverBlockers = $cutover['blockers']
        }
    }
    else {
        $cutoverBlockersProperty = $cutover.PSObject.Properties['blockers']
        if ($null -ne $cutoverBlockersProperty) {
            $cutoverBlockers = $cutoverBlockersProperty.Value
        }
    }
    if ($cutoverStatus -notin @('CUTOVER_BLOCKED', 'WORKFLOW_CUTOVER_COMPLETE') -or
        -not (Test-DeliveryMemberExists -InputObject $cutover -Name 'blockers') -or
        $null -eq $cutoverBlockers -or
        $cutoverBlockers -is [string] -or
        $cutoverBlockers -isnot [System.Collections.IEnumerable]) {
        throw 'State has an invalid cutover record.'
    }
    if ($cutoverStatus -eq 'WORKFLOW_CUTOVER_COMPLETE' -and @($cutoverBlockers).Count -gt 0) {
        throw 'State cannot declare a completed cutover while blockers remain.'
    }

    $vocabulary = Get-DeliveryMemberValue -InputObject $Config -Name 'vocabulary'
    $currentSprint = Get-DeliveryMemberValue -InputObject $State -Name 'currentSprint'
    if (-not (Test-DeliveryMemberExists -InputObject $currentSprint -Name 'name') -or [string]::IsNullOrWhiteSpace((Get-DeliveryMemberValue -InputObject $currentSprint -Name 'name'))) {
        throw 'State currentSprint is missing name.'
    }
    $workItems = Get-DeliveryMemberValue -InputObject $currentSprint -Name 'workItems'
    if ($null -eq $workItems) {
        throw 'State currentSprint is missing workItems.'
    }

    foreach ($workItem in @($workItems)) {
        foreach ($name in 'id', 'name', 'risk', 'lifecycle', 'stage', 'evidenceBindings', 'retryCounters', 'gates', 'blockers') {
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

            if ($bindingStatus -eq 'CURRENT' -and ($null -ne $environment -or $null -ne $workflowRun)) {
                $artifact = Get-DeliveryMemberValue -InputObject $binding -Name 'artifact'
                if ([string]::IsNullOrWhiteSpace($artifact)) {
                    throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' has current deployment evidence without an immutable artifact."
                }
            }
        }

        $hasHistoricalOnlyEvidence = @($evidenceBindings | Where-Object {
            (Get-DeliveryMemberValue -InputObject $_ -Name 'status') -eq 'HISTORICAL_UNVERIFIED'
        }).Count -gt 0
        if ($lifecycle -in @('DEV_DEPLOYED', 'QA_DEPLOYED', 'RELEASED') -and -not $hasHistoricalOnlyEvidence) {
            $currentDeploymentEvidence = @($evidenceBindings | Where-Object {
                (Get-DeliveryMemberValue -InputObject $_ -Name 'status') -eq 'CURRENT' -and
                $null -ne (Get-DeliveryMemberValue -InputObject $_ -Name 'environment') -and
                $null -ne (Get-DeliveryMemberValue -InputObject $_ -Name 'workflowRun') -and
                -not [string]::IsNullOrWhiteSpace((Get-DeliveryMemberValue -InputObject $_ -Name 'artifact'))
            })
            if ($currentDeploymentEvidence.Count -eq 0) {
                throw "Work item '$((Get-DeliveryMemberValue -InputObject $workItem -Name 'id'))' lifecycle '$lifecycle' requires current immutable evidence."
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
    $cutover = Get-DeliveryMemberValue -InputObject $State -Name 'cutover'
    if ((Get-DeliveryMemberValue -InputObject $cutover -Name 'status') -ne 'WORKFLOW_CUTOVER_COMPLETE') {
        return [pscustomobject]@{
            kind = 'CUTOVER_BLOCKED'
            workItemId = $null
            reason = 'Sprint work cannot resume until the workflow cutover is complete and reconciled.'
            sideEffect = $false
        }
    }
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

function Test-WorkflowLifecycleOwner {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)][string] $ExpectedOwner
    )

    $owner = Get-DeliveryMemberValue -InputObject $State -Name 'workflowLifecycleOwner'
    if ([string]::IsNullOrWhiteSpace($owner) -or $owner -cne $ExpectedOwner) {
        throw "Competing workflow lifecycle owner '$owner' was found; expected '$ExpectedOwner'."
    }

    return $true
}

function Get-LocalWorktreeRecords {
    [OutputType([object[]])]
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    $records = [System.Collections.Generic.List[object]]::new()
    $current = $null
    foreach ($line in @(& git -C $RepositoryRoot worktree list --porcelain)) {
        if ($line -like 'worktree *') {
            if ($null -ne $current) {
                $records.Add([pscustomobject]$current)
            }
            $current = @{ path = $line.Substring(9); branch = $null; head = $null }
        }
        elseif ($null -ne $current -and $line -like 'HEAD *') {
            $current.head = $line.Substring(5)
        }
        elseif ($null -ne $current -and $line -like 'branch refs/heads/*') {
            $current.branch = $line.Substring(18)
        }
    }

    if ($null -ne $current) {
        $records.Add([pscustomobject]$current)
    }

    return @($records)
}

function Resolve-PreservedProductWorktreeSnapshot {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)] $CutoverEvidence,
        [scriptblock] $WorktreeProvider
    )

    $observations = Get-DeliveryMemberValue -InputObject $CutoverEvidence -Name 'observations'
    $preserved = Get-DeliveryMemberValue -InputObject $observations -Name 'preservedProductWorktree'
    $sourceWorktree = Get-DeliveryMemberValue -InputObject $preserved -Name 'path'
    $featureBranch = Get-DeliveryMemberValue -InputObject $preserved -Name 'branch'
    $productHead = Get-DeliveryMemberValue -InputObject $preserved -Name 'commit'
    $snapshot = [ordered]@{
        isPreserved = -not ([string]::IsNullOrWhiteSpace($sourceWorktree) -or [string]::IsNullOrWhiteSpace($featureBranch) -or [string]::IsNullOrWhiteSpace($productHead))
        sourceWorktree = $sourceWorktree
        featureBranch = $featureBranch
        productHead = $productHead
        localWorktreePresent = $false
        validationError = $null
    }

    if (-not $snapshot.isPreserved) {
        $snapshot.validationError = 'PRESERVED_PRODUCT_WORKTREE_EVIDENCE_UNAVAILABLE'
        return [pscustomobject]$snapshot
    }

    $worktrees = if ($null -ne $WorktreeProvider) { @(& $WorktreeProvider) } else { Get-LocalWorktreeRecords -RepositoryRoot $RepositoryRoot }
    $localWorktree = @($worktrees | Where-Object { (Get-DeliveryMemberValue -InputObject $_ -Name 'branch') -ceq $featureBranch }) | Select-Object -First 1
    if ($null -eq $localWorktree) {
        return [pscustomobject]$snapshot
    }

    $snapshot.localWorktreePresent = $true
    $localHead = Get-DeliveryMemberValue -InputObject $localWorktree -Name 'head'
    if ([string]::IsNullOrWhiteSpace($localHead) -or $localHead -cne $productHead) {
        $snapshot.isPreserved = $false
        $snapshot.validationError = 'PRESERVED_PRODUCT_WORKTREE_HEAD_MISMATCH'
    }

    return [pscustomobject]$snapshot
}

function Get-CutoverProofStatus {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] $CutoverEvidence,
        [Parameter(Mandatory)] $Snapshot
    )

    $observations = Get-DeliveryMemberValue -InputObject $CutoverEvidence -Name 'observations'
    $baseline = $null
    $selfTests = $null
    $developmentRun = $null
    if ($null -ne $observations) {
        $baseline = Get-DeliveryMemberValue -InputObject $observations -Name 'postMigrationBaseline'
        if ($null -ne $baseline) {
            $selfTests = Get-DeliveryMemberValue -InputObject $baseline -Name 'workflowSelfTests'
        }

        $github = Get-DeliveryMemberValue -InputObject $observations -Name 'github'
        if ($null -ne $github) {
            $developmentRun = Get-DeliveryMemberValue -InputObject $github -Name 'developmentRun'
        }
    }
    $expectedCommit = if ($null -ne $developmentRun) { Get-DeliveryMemberValue -InputObject $developmentRun -Name 'commit' } else { $null }
    $expectedRun = if ($null -ne $developmentRun) { Get-DeliveryMemberValue -InputObject $developmentRun -Name 'number' } else { $null }
    $deployments = @(Get-DeliveryMemberValue -InputObject $Snapshot -Name 'deployments')
    $matchingDeployment = @($deployments | Where-Object {
        (Get-DeliveryMemberValue -InputObject $_ -Name 'commit') -ceq $expectedCommit -and
        (Get-DeliveryMemberValue -InputObject $_ -Name 'workflowRun') -eq $expectedRun -and
        (Get-DeliveryMemberValue -InputObject $_ -Name 'environment') -eq 'development' -and
        -not [string]::IsNullOrWhiteSpace((Get-DeliveryMemberValue -InputObject $_ -Name 'artifact'))
    })

    $baselineStatus = if ($null -ne $baseline) { Get-DeliveryMemberValue -InputObject $baseline -Name 'status' } else { $null }

    return [pscustomobject]@{
        postMigrationBaseline = $baselineStatus -in @('PASS', 'PASS_WITH_ENVIRONMENT_FAILURE')
        selfTestsPassed = $null -ne $selfTests -and (Get-DeliveryMemberValue -InputObject $selfTests -Name 'status') -eq 'PASS' -and (Get-DeliveryMemberValue -InputObject $selfTests -Name 'failed') -eq 0
        selfTestEvidenceAvailable = $null -ne $selfTests
        authoritativeDeploymentEvidenceAvailable = $matchingDeployment.Count -gt 0
    }
}

function Set-WorkflowCutover {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $Config,
        [Parameter(Mandatory)] $WorktreeSnapshot,
        [Parameter(Mandatory)] $Baselines,
        [Parameter(Mandatory)][bool] $SelfTestsPassed,
        [bool] $SelfTestEvidenceAvailable = $false,
        [Parameter(Mandatory)] $Reconciliation,
        [bool] $AuthoritativeDeploymentEvidenceAvailable = $true
    )

    Test-SprintDeliveryState -State $State -Config $Config | Out-Null
    Test-WorkflowLifecycleOwner -State $State -ExpectedOwner 'sprint-orchestrator' | Out-Null

    $blockers = [System.Collections.Generic.List[string]]::new()
    if ((Get-DeliveryMemberValue -InputObject $WorktreeSnapshot -Name 'isPreserved') -ne $true -or
        [string]::IsNullOrWhiteSpace((Get-DeliveryMemberValue -InputObject $WorktreeSnapshot -Name 'sourceWorktree')) -or
        [string]::IsNullOrWhiteSpace((Get-DeliveryMemberValue -InputObject $WorktreeSnapshot -Name 'featureBranch')) -or
        [string]::IsNullOrWhiteSpace((Get-DeliveryMemberValue -InputObject $WorktreeSnapshot -Name 'productHead'))) {
        $blockers.Add('Preserved product worktree snapshot is incomplete.')
    }

    if ((Get-DeliveryMemberValue -InputObject $Baselines -Name 'preMigration') -ne $true) {
        $blockers.Add('Pre-migration baseline is unavailable.')
    }
    if ((Get-DeliveryMemberValue -InputObject $Baselines -Name 'postMigration') -ne $true) {
        $blockers.Add('Post-migration baseline is unavailable.')
    }
    if ((Get-DeliveryMemberValue -InputObject $Baselines -Name 'ci') -eq 'FAIL') {
        $blockers.Add('CI status is FAIL after local validation.')
    }
    if (-not $SelfTestsPassed) {
        $blockers.Add('SELF_TESTS_FAILED')
    }
    elseif (-not $SelfTestEvidenceAvailable) {
        $blockers.Add('SELF_TEST_EVIDENCE_UNAVAILABLE')
    }
    if (-not $AuthoritativeDeploymentEvidenceAvailable) {
        $blockers.Add('AUTHORITATIVE_AZURE_DEPLOYMENT_SNAPSHOT_UNAVAILABLE')
    }

    $reconciliationKind = Get-DeliveryMemberValue -InputObject $Reconciliation -Name 'kind'
    if ($reconciliationKind -ne 'STATE_RECONCILIATION_AGREES') {
        $contradictions = @(Get-DeliveryMemberValue -InputObject $Reconciliation -Name 'contradictions')
        if ($contradictions.Count -eq 0) {
            $blockers.Add("Reconciliation result is '$reconciliationKind'.")
        }
        foreach ($contradiction in $contradictions) {
            $kind = Get-DeliveryMemberValue -InputObject $contradiction -Name 'kind'
            $blockers.Add("Reconciliation contradiction: $kind")
        }
    }

    $derivedState = Copy-DeliveryObject -InputObject $State
    $derivedCutover = Get-DeliveryMemberValue -InputObject $derivedState -Name 'cutover'
    $derivedCutover.status = if ($blockers.Count -eq 0) { 'WORKFLOW_CUTOVER_COMPLETE' } else { 'CUTOVER_BLOCKED' }
    $derivedCutover.blockers = @($blockers)

    return [pscustomobject]@{
        status = $derivedCutover.status
        blockers = @($blockers)
        state = $derivedState
        sideEffect = $false
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $config = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/config.json')
    $state = Read-DeliveryJson -Path (Join-Path $repositoryRoot 'delivery/state.json')
    Test-SprintDeliveryState -State $state -Config $config | Out-Null
    $reconciliation = $null
    if ($Reconcile) {
        $snapshotInput = if ([string]::IsNullOrWhiteSpace($ReconciliationSnapshotPath)) { $null } else { Read-DeliveryJson -Path $ReconciliationSnapshotPath }
        $snapshot = if ($null -eq $snapshotInput) {
            Get-AuthoritativeSnapshot -RepositoryRoot $repositoryRoot
        }
        else {
            Get-AuthoritativeSnapshot -RepositoryRoot $repositoryRoot -GitHubSnapshotProvider { Get-DeliveryMemberValue -InputObject $snapshotInput -Name 'github' } -AzureSnapshotProvider { Get-DeliveryMemberValue -InputObject $snapshotInput -Name 'azure' }
        }
        $reconciliation = Compare-DeliveryState -State $state -Snapshot $snapshot
    }

    $cutover = $null
    if ($Reconcile) {
        $resolvedCutoverEvidencePath = if ([string]::IsNullOrWhiteSpace($CutoverEvidencePath)) { Join-Path $repositoryRoot 'delivery/evidence/cutover-validation.json' } else { $CutoverEvidencePath }
        $cutoverEvidence = Read-DeliveryJson -Path $resolvedCutoverEvidencePath
        $worktreeSnapshot = Resolve-PreservedProductWorktreeSnapshot -RepositoryRoot $repositoryRoot -CutoverEvidence $cutoverEvidence
        $proof = Get-CutoverProofStatus -CutoverEvidence $cutoverEvidence -Snapshot $snapshot
        $cutover = Set-WorkflowCutover -State $state -Config $config -WorktreeSnapshot $worktreeSnapshot -Baselines @{
            preMigration = Test-Path -LiteralPath (Join-Path $repositoryRoot 'delivery/evidence/pre-migration-baseline.json')
            postMigration = $proof.postMigrationBaseline
        } -SelfTestsPassed $proof.selfTestsPassed -SelfTestEvidenceAvailable $proof.selfTestEvidenceAvailable -Reconciliation $reconciliation -AuthoritativeDeploymentEvidenceAvailable $proof.authoritativeDeploymentEvidenceAvailable
    }

    $actionState = if ($null -ne $cutover) { $cutover.state } else { $state }
    $action = Get-NextDeliveryAction -State $actionState -Config $config

    [pscustomobject]@{
        mode = if ($WhatIf) { 'WHAT_IF' } else { 'READ_ONLY' }
        sprint = (Get-DeliveryMemberValue -InputObject (Get-DeliveryMemberValue -InputObject $state -Name 'currentSprint') -Name 'id')
        action = $action
        reconciliation = $reconciliation
        cutover = $cutover
    } | ConvertTo-Json -Depth 8
}
