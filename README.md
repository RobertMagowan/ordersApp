# CloudOrders

CloudOrders is a .NET 10/Azure order-processing system. The repository is being built in independently testable sprints; see the [sprint implementation plan](docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md) and [design specification](docs/superpowers/specs/2026-08-16-cloudorders-greenfield-design.md).

## Branch promotion and deployment

Work in `feature/*` branches. Open a pull request into `development`, then promote only through pull requests to `test` and finally `master`. Protected branches require pull requests, the CI plus promotion-policy checks, and conversation resolution; this single-developer repository requires zero independent approvals. Merges start the matching GitHub environment workflow (`development`, `test`, or `production`).

Repository administrators may explicitly bypass the review requirement for their own PR; CI, promotion-policy, and conversation checks remain required because this repository has a single developer.

The Azure workflow uses OIDC and remains safely disabled until each environment has `AZURE_DEPLOYMENT_ENABLED=true`, the federated identity secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), and these variables: `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `AZURE_APP_NAME`, `AZURE_ACR_NAME`, `AZURE_MANAGED_ENVIRONMENT_NAME`, and `AZURE_LOG_ANALYTICS_NAME`. It previews changes with `what-if`, deploys the pinned AVM composition in `infra/main.bicep`, builds the API image from `src/CloudOrders.Api/Dockerfile`, pushes it to ACR, and smoke-tests `/health/live`.

Deployment summaries retain the Git release SHA, unique Bicep deployment name, immutable image digest reference, Container App revision, previous image/revision rollback reference, and API endpoint without exposing secrets. Rollback state comes from the latest ready revision's own template rather than from the app's mutable candidate template, and summary steps run even when inspection, preparation, deployment, or smoke testing fails. An existing Container App remains on its current release while the replacement image is built and previewed; the public bootstrap image is used only for a first deployment and is tagged `release=bootstrap`, never as the candidate Git release. Manual deployment is rejected unless started from `development`, `test`, or `master`.

The workflow uses three ordered jobs as explicit review boundaries:

1. `preview_foundation` authenticates with OIDC, fails closed on lookup errors other than Azure's explicit `ResourceNotFound`, captures the latest ready rollback revision/image, and runs the mutation-free foundation what-if.
2. `prepare_release` begins only after that job succeeds. It provisions the bootstrap foundation only when required, publishes the digest-backed image, and runs the exact immutable-release what-if without changing the candidate Container App release.
3. `deploy_release` is a separate downstream environment job. It cannot start before the digest preview completes, applies the environment's deployment protection again, deploys the reviewed digest, and smoke-tests it.

### Manual test-release inspection and gate

Inspect names and protection state without printing secret values:

```powershell
gh variable list --env test
gh secret list --env test
$repository = gh repo view --json nameWithOwner --jq .nameWithOwner
gh api "repos/$repository/environments/test/deployment-branch-policies"
az group show --name ordersapp-test --query '{name:name,location:location,state:properties.provisioningState}'
az resource list --resource-group ordersapp-test --query '[].{name:name,type:type,location:location}' --output table
```

The required release gate is ordered and cannot be replaced by a direct protected-branch push or a feature-branch workflow dispatch:

1. Open and review `feature/*` → `development`; required CI, promotion-policy, and conversation-resolution checks must pass.
2. Merge the PR and retain the successful `development` deployment summary. A separate smoke-test agent verifies the reported HTTPS endpoint and immutable digest.
3. Open and review `development` → `test`; the same required checks must pass.
4. Merge the PR. The `test` environment branch restriction admits only `test`, and OIDC deploys into `ordersapp-test` without a long-lived Azure credential.
5. Retain the test deployment summary and run the QA-only matrix before any `test` → `master` promotion.

For a first test deployment, review the foundation job's what-if (Log Analytics, ACR, Container Apps environment, and Container App), then review the preparation job's second what-if against the resolved digest before the downstream deployment job proceeds. Do not treat a local what-if or manually provisioned resource as a substitute for the protected promotion gate.

## Version-1 contract pack

The repository-owned [frontend design contract](docs/contracts/frontend-design.md), [version-1 contracts](docs/contracts/v1-contracts.md), and [traceability map](docs/contracts/traceability.md) are the authoritative implementation contracts. The `test` environment is the staging-equivalent environment used throughout this repository.

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
