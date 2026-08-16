# CloudOrders

CloudOrders is a .NET 10/Azure order-processing system. The repository is being built in independently testable sprints; see the [sprint implementation plan](docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md) and [design specification](docs/superpowers/specs/2026-08-16-cloudorders-greenfield-design.md).

## Branch promotion and deployment

Work in `feature/*` branches. Open a pull request into `development`, then promote only through pull requests to `test` and finally `master`. Protected branches require pull requests, the CI plus promotion-policy checks, and conversation resolution; this single-developer repository requires zero independent approvals. Merges start the matching GitHub environment workflow (`development`, `test`, or `production`).

Repository administrators may explicitly bypass the review requirement for their own PR; CI, promotion-policy, and conversation checks remain required because this repository has a single developer.

The Azure workflow uses OIDC and remains safely disabled until each environment has `AZURE_DEPLOYMENT_ENABLED=true`, the federated identity secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), and these variables: `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `AZURE_APP_NAME`, `AZURE_ACR_NAME`, `AZURE_MANAGED_ENVIRONMENT_NAME`, and `AZURE_LOG_ANALYTICS_NAME`. It previews changes with `what-if`, deploys the pinned AVM composition in `infra/main.bicep`, builds the API image from `src/CloudOrders.Api/Dockerfile`, pushes it to ACR, and smoke-tests `/health/live`.

## Current status

Sprint 1 provides a manually runnable local API vertical slice with in-memory order persistence and a deployable Azure Container Apps MVP foundation using pinned AVM Bicep modules. SQL durability is scheduled for a later sprint.

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
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
```

Do not commit secrets or generated build output. Cloud resource values and deployment identities are supplied at the sprint decision gates documented in `AGENTS.md`.
