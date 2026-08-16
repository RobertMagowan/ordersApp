# AVM Infrastructure Refactor Design

## Goal

Refactor the Azure infrastructure into focused Bicep modules that use pinned Azure Verified Modules (AVM) wherever those modules cover the required resources, while preserving the existing deployment workflow and environment contract.

## Scope

- Keep `infra/main.bicep` as the resource-group-scoped composition root.
- Split infrastructure by responsibility under `infra/modules/`:
  - Observability and Log Analytics workspace.
  - Azure Container Registry.
  - Container Apps managed environment.
  - Container App workload, identity, ingress, probes, and scaling.
  - Registry-scoped ACR access as a separate authorization declaration.
- Record the approved AVM module references and pinned versions in `infra/avm-versions.md`; keep literal versions in each module declaration because Bicep module source references are not safely parameterized.
- Add `infra/bicepconfig.json` with a public AVM registry alias and linter configuration.
- Preserve all existing parameters and outputs consumed by `.github/workflows/deploy.yml` and the environment parameter files.
- Add a pull-request Bicep validation workflow that runs formatting/lint/build checks without Azure credentials.

## Deployment Design

The existing two-phase deployment remains intentional. The first pass creates the registry, managed environment, and Container App using the public bootstrap image. The workflow then grants the Container App managed identity `AcrPull`, pushes the application image, and performs a second deployment using the private ACR image. The AVM Container App module will receive the same identity, registry, ingress, probe, and scaling settings in both phases.

The ACR `AcrPull` assignment is scoped to the registry, not the Container App. The published AVM authorization role-assignment modules do not target an individual resource scope, so the implementation will retain the small native `Microsoft.Authorization/roleAssignments` declaration scoped to the registry. It will not be incorrectly placed in the Container App module's app-scoped role assignments.

Native Bicep declarations may remain only where AVM lacks the required resource scope, sequencing, or compatibility support. Each exception must be documented next to the declaration.

## Validation and Compatibility

- Pin every AVM module reference to an explicit tested version.
- Build the composition root with `az bicep build`.
- Add `infra/environments/test.bicepparam` and `infra/environments/production.bicepparam` alongside the existing development file. Non-secret deployment values may be overridden by workflow variables, but each overlay must provide a complete parameter contract suitable for a resource-group `what-if`.
- Run an Azure resource-group `what-if` for development, test, and production parameter sets where credentials and resource groups are available.
- Run the pull-request Bicep validation workflow on every infrastructure change.
- Run the existing .NET build and tests to ensure the infrastructure-only change does not affect application packaging.
- Keep deployment outputs (`containerAppName`, `containerAppFqdn`, `registryName`, and `registryLoginServer`) unchanged.

## Non-Goals

This sprint does not change application code, Azure resource naming, networking topology, database design, or GitHub promotion policy.
