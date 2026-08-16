# CloudOrders

CloudOrders is a .NET 10/Azure order-processing system. The repository is being built in independently testable sprints; see the [sprint implementation plan](docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md) and [design specification](docs/superpowers/specs/2026-08-16-cloudorders-greenfield-design.md).

## Branch promotion and deployment

Work in `feature/*` or `agent/*` branches. Open a pull request into `development`, then promote only through approved pull requests to `test` and finally `master`. Each protected branch requires exactly one approval and the CI plus promotion-policy checks. Merges start the matching GitHub environment workflow (`development`, `test`, or `production`).

The Azure workflow uses OIDC and remains safely disabled until each environment has `AZURE_DEPLOYMENT_ENABLED=true`, the Azure federated identity secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), and the environment variables `AZURE_RESOURCE_GROUP`, `AZURE_CONTAINER_APP_NAME`, and `CONTAINER_IMAGE`.

## Current status

Sprint 1 provides a manually runnable local API vertical slice with in-memory order persistence. SQL durability and Azure deployment are scheduled for later sprints.

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
