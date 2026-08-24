# Non-production Foundation Reset Design

## Goal

Recreate the deleted CloudOrders development and test Azure foundations before further Sprint 3 delivery work, without changing production.

## Scope

- Create only `ordersapp-development` and `ordersapp-test` in `ukwest`.
- Reapply the already-reviewed Bicep composition through the protected GitHub deployment workflow from `development` and `test`.
- Use the existing GitHub environment OIDC configuration and required deployment approvals.
- Verify the recreated API, ACR, Container Apps environment, Log Analytics workspace, managed identity, TLS, and health endpoint in each non-production environment.

## Exclusions

- Do not create, delete, or modify `ordersapp-production` or production GitHub environment settings.
- Do not introduce Azure SQL, change application code, or start Sprint 3 feature delivery as part of the reset.
- Do not bypass the deployment workflow with a direct Bicep deployment.

## Execution Design

Azure CLI creates the two empty resource groups, since the existing workflow assumes its resource group already exists. The workflow is then manually dispatched from each protected promotion branch. Its existing three approval boundaries retain the normal OIDC login, Bicep preview, foundation deployment, immutable image publication, candidate-revision validation, and HTTPS liveness smoke.

The reset is accepted only when both environments show a healthy, 100%-traffic Container App revision whose release/digest identities agree with the workflow summary; `/health/live` returns HTTPS 200 with normal certificate validation. Any failed environment remains isolated and is corrected before the other is changed.

## Rollback and Cost Control

The reset creates the minimal existing MVP foundation only. It remains explicitly limited to development and test; production is never a fallback target. Subsequent Sprint 3 Azure SQL changes remain a separate reviewed task and require their own deployment evidence.
