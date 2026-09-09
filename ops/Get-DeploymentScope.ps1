[CmdletBinding()]
param(
    [string[]] $ChangedPaths,
    [bool] $ComparisonAvailable = $false
)

Set-StrictMode -Version Latest

function Get-DeploymentScope {
    [CmdletBinding()]
    param(
        [string[]] $ChangedPaths,
        [Parameter(Mandatory)] [bool] $ComparisonAvailable
    )

    if (-not $ComparisonAvailable) {
        return [pscustomobject]@{ deployable = $true; reason = 'comparison_unavailable' }
    }

    $deployablePath = {
        param([string] $Path)
        $Path -match '^(src|tests|infra|local|ops/releases)/' -or
        $Path -in @('ops/Get-DeploymentScope.ps1', '.dockerignore', '.github/workflows/deploy.yml', 'global.json', 'NuGet.config', 'Directory.Build.props', 'Directory.Build.targets') -or
        $Path -match '(^|/)(Dockerfile(?:\..*)?|docker-compose(?:\..*)?|[^/]+\.(?:sln|slnx|csproj|fsproj|vbproj|props|targets|proj))$'
    }

    $deliveryOnlyPath = {
        param([string] $Path)
        $Path -match '^docs/' -or $Path -match '^delivery/' -or $Path -eq 'AGENTS.md' -or $Path -match '^\.agents/skills/' -or $Path -match '^\.superpowers/sdd/' -or
        $Path -match '^\.github/workflows/' -and $Path -ne '.github/workflows/deploy.yml' -or
        $Path -match '^ops/tests/' -or
        $Path -match '^ops/[^/]+\.Tests\.ps1$' -or
        $Path -match '^ops/Test-[^/]+\.ps1$'
    }

    $paths = @($ChangedPaths | Where-Object { $null -ne $_ } | ForEach-Object {
        ([string]$_).Trim() -replace '\\', '/' -replace '^\./', ''
    } | Where-Object { $_ -ne '' })

    $unknown = @($paths | Where-Object { -not (& $deployablePath $_) -and -not (& $deliveryOnlyPath $_) })
    if ($unknown.Count -gt 0) {
        return [pscustomobject]@{ deployable = $true; reason = 'unknown_path' }
    }

    if (@($paths | Where-Object { & $deployablePath $_ }).Count -gt 0) {
        return [pscustomobject]@{ deployable = $true; reason = 'deployable_path' }
    }

    return [pscustomobject]@{ deployable = $false; reason = 'delivery_only' }
}

if ($MyInvocation.InvocationName -ne '.') {
    $json = [Console]::In.ReadToEnd()
    if (-not [string]::IsNullOrWhiteSpace($json)) {
        $request = $json | ConvertFrom-Json
        Get-DeploymentScope -ChangedPaths @($request.ChangedPaths) -ComparisonAvailable ([bool]$request.ComparisonAvailable) |
            ConvertTo-Json -Compress
    }
}
