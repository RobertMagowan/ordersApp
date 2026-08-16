# CloudOrders

CloudOrders is a .NET 10/Azure order-processing system. The repository is being built in independently testable sprints; see the [sprint implementation plan](docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md) and [design specification](docs/superpowers/specs/2026-08-16-cloudorders-greenfield-design.md).

## Current status

Sprint 0 bootstraps the solution, repository policy, and architecture tests. The application is not yet deployed to Azure.

## Prerequisites

- .NET SDK 10.0.303 or a compatible stable .NET 10 feature band
- Git 2.50+
- Docker Desktop for later local infrastructure sprints
- Azure CLI and Azure Functions Core Tools for later deployment sprints

## Verify the repository

From `C:\\repos\\OrderApp`:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test --configuration Release
```

Do not commit secrets or generated build output. Cloud resource values and deployment identities are supplied at the sprint decision gates documented in `AGENTS.md`.
