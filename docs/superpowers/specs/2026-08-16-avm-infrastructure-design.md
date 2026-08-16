# AVM Infrastructure Refactor Design

## Goal

Refactor the Azure infrastructure into focused Bicep modules that use pinned Azure Verified Modules (AVM) wherever those modules cover the required resources, while preserving the existing deployment workflow and environment contract.

## Scope

- Keep `infra/main.bicep` as the resource-group-scoped composition root.
- Split infrastructure by responsibility under `infra/modules/`:
  - Observability and Log Analytics workspace.
  - Azure Container Registry.
  - Container Apps managed environment.
  - Container App workload, identity, ingress, probes, scaling, and ACR access.
- Centralize AVM module versions in `infra/avm-versions.bicep`.
- Add `infra/bicepconfig.json` with a public AVM registry alias and linter configuration.
- Preserve all existing parameters and outputs consumed by `.github/workflows/deploy.yml` and the environment parameter files.

## Deployment Design

The existing two-phase deployment remains intentional. The first pass creates the registry, managed environment, and Container App using the public bootstrap image. The workflow then grants the Container App managed identity `AcrPull`, pushes the application image, and performs a second deployment using the private ACR image. The AVM Container App module will receive the same identity, registry, ingress, probe, and scaling settings in both phases.

Native Bicep declarations may remain only where AVM does not expose a required sequencing or compatibility feature. Any such exception must be documented next to the declaration.

## Validation and Compatibility

- Pin every AVM module reference to an explicit tested version.
- Build the composition root with `az bicep build`.
- Run an Azure resource-group `what-if` for each environment parameter set where credentials and resource groups are available.
- Run the existing .NET build and tests to ensure the infrastructure-only change does not affect application packaging.
- Keep deployment outputs (`containerAppName`, `containerAppFqdn`, `registryName`, and `registryLoginServer`) unchanged.

## Non-Goals

This sprint does not change application code, Azure resource naming, networking topology, database design, or GitHub promotion policy.
