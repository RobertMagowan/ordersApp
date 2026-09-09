# Non-production Cost Controls — Development Verification

Date: 2026-09-09

Release: `36268ba1664fa4d264a9c5294dc527742a7358b0`
Deployment: [GitHub Actions run 34379999043](https://github.com/RobertMagowan/ordersApp/actions/runs/34379999043)

## Scope

This release reduces idle cost only in development and test. It does not use the Azure SQL free offer, share SQL logical servers, alter production, or change the promotion workflow.

## Local Verification

- `Invoke-Pester ./ops/tests/NonProductionCostControls.Tests.ps1 -PassThru`: 2 passed.
- `az bicep lint --file infra/main.bicep` and Bicep builds for the composition root plus development, test, and production parameter overlays: passed.
- `dotnet format --verify-no-changes`, Release build, and tests: 37 architecture, 19 unit, and 86 Docker-backed integration tests passed.

## Development Deployment and Smoke Test

The deployment completed every stage successfully: foundation preview, immutable release preview, SQL preview/provisioning/migration, candidate deployment, and pipeline health smoke test.

Independent post-deployment inspection recorded:

- Container App `cloudorders-dev-api`: `minReplicas = 0`, `maxReplicas = 2`; latest and latest-ready revision `cloudorders-dev-api--0000012`.
- Azure SQL `CloudOrders`: General Purpose Serverless with `minCapacity = 0.5` and `autoPauseDelay = 15` minutes.
- `GET https://cloudorders-dev-api.purplehill-dc6cd696.ukwest.azurecontainerapps.io/health/live`: HTTP 200 with `{\"status\":\"ok\"}`.

The workflow's candidate and bounded `/health/live` retry gates provide the deployed on-demand-start verification. The separate `development` → `test` promotion remains required before the test environment is changed.
