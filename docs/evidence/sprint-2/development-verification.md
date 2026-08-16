# Sprint 2 development verification

Recorded: 2026-08-16T19:01:17Z

Candidate branch: `feature/replan-sprint-assurance`

Candidate base before Task 2: `92dd95a3cf944a532dbd279eb1dec19f56db7e3b`

## Outcome

The three-day-equivalent developer-style verification matrix passed for the local workflow/Bicep candidate and direct configuration inspection. This is local verification, not independent approval and not a live-release smoke gate. The candidate still requires the protected PR sequence before Azure deployment.

## Day-equivalent 1 — workflow and regression behavior

- Baseline `dotnet test --configuration Release`: 13 passed, 0 failed.
- TDD red: the focused release-state policy test failed because the workflow had no existing-release inspection.
- TDD green: `DeploymentWorkflowPreservesReleaseAndRollbackState` passed after the minimal implementation.
- Direct workflow inspection confirmed full-SHA-pinned checkout/login actions, job-scoped OIDC permission, promotion-ref validation, TLS-verifying curl, digest deployment, and no long-lived Azure credentials.
- Direct state-transition inspection confirmed that an existing Container App skips the public bootstrap deployment, only explicit `ResourceNotFound` permits bootstrapping, and all other lookup failures stop the run.
- Rollback inspection resolves `latestReadyRevisionName` and queries that revision's own template for the image. Preview, preparation, and deployment summaries use `always()` so the retained rollback state survives downstream failures.
- Separate ordered jobs put foundation provisioning after the foundation what-if and candidate deployment after the resolved-digest what-if.
- `npx.cmd --yes prettier@3.6.2 .github/workflows/deploy.yml --parser yaml` parsed the workflow successfully.

## Day-equivalent 2 — Bicep and deployment plan

The following completed with exit code 0:

```powershell
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
```

Generated ARM JSON was removed and is not retained. The root template applies `releaseId` only to Container App tags, so a release does not churn the shared registry, environment, or workspace tags.

The authorized read-only test what-if completed with exit code 0 against `ordersapp-test` in UK West using the existing test overlay and bootstrap inputs. Reviewed result: exactly four creates, no deletes or modifications:

- `Microsoft.OperationalInsights/workspaces/cloudorders-test-logs`
- `Microsoft.ContainerRegistry/registries/cloudorderst583431testacr`
- `Microsoft.App/managedEnvironments/cloudorders-test-env`
- `Microsoft.App/containerApps/cloudorders-test-api`

The final digest-backed what-if cannot exist until the protected workflow publishes the image. The preparation job logs that exact what-if, then only a separate downstream environment job can deploy it.

## Day-equivalent 3 — GitHub, OIDC, RBAC, and artifact state

Sanitized direct inspection established:

- GitHub environment `test` has `AZURE_DEPLOYMENT_ENABLED=true`, all seven required resource variables, and the three required Azure secret names.
- The environment has a custom deployment branch policy admitting only branch `test`.
- Protected branch `test` requires `Restore, format, build, and test`, `Enforce promotion source branch`, pull-request review configuration, and conversation resolution.
- Entra application `ordersApp-github-actions-test` has one GitHub Actions federated credential with the `environment:test` subject and token-exchange audience.
- Its service principal has Contributor and Role Based Access Control Administrator scoped to `ordersapp-test`; no long-lived credential was added.
- `ordersapp-test` exists, is tagged for CloudOrders/test/Bicep, and contains no resources or deployment history before promotion.
- Prior live development baseline run `31963067907` succeeded at release `30ae0a033978f92f3fa0aa0e6bd53909701251fb`; it provisioned four resources, published an image, and returned HTTP 200 from `/health/live`. That run predates the Task 1/2 candidate and is not evidence that this candidate deployed.

## Limitations and gate

`actionlint` was not preinstalled; deterministic .NET policy tests plus a Prettier YAML parse were used locally. GitHub remains the authoritative workflow-expression validator during the required PR checks. No protected branch was pushed, no PR was created, and no Azure resource was changed during this verification.
