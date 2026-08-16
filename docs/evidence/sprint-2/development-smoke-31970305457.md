# Sprint 2 development post-deployment smoke — run 31970305457

Date: 2026-08-16 (UTC)

Status: **PASS** for the independently observed deployed runtime, with one deployment-workflow evidence defect recorded below.

Scope: read-only inspection of GitHub/Azure state plus one synthetic, credential-free create/read journey against the in-memory development API. No code, workflow, infrastructure, Azure resource, or branch changes were made by this smoke-test activity.

## Release under test

- GitHub run: <https://github.com/RobertMagowan/ordersApp/actions/runs/31970305457>
- Workflow: `Deploy CloudOrders MVP`
- Branch: `development`
- Release commit: `32b5d3c82168e1d75367d4c0e6db67b1fa9b0a44`
- Endpoint: <https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io>

Command:

```powershell
gh run view 31970305457 --repo RobertMagowan/ordersApp --json conclusion,status,headSha,headBranch,workflowName,url,jobs
```

Observed result: run `completed/success`; the deployment job and its deployment/smoke steps were reported successful.

## TLS and HTTP probes

The endpoint certificate was inspected using `.NET SslStream.AuthenticateAsClient()` with a validation callback that returned true only when `SslPolicyErrors` was `None`.

Observed result:

- TLS protocol: `Tls13`
- validation errors: `None`
- hostname/chain validation: passed
- certificate subject: `CN=mangowater-09c0bad4.ukwest.azurecontainerapps.io, O=Microsoft Corporation, L=Redmond, S=WA, C=US`
- validity: `2026-08-16T02:05:11Z` through `2027-02-12T02:05:11Z`

Commands:

```powershell
Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/health/live' -Method Get -MaximumRedirection 0
Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/health/ready' -Method Get -MaximumRedirection 0
Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/openapi/v1.json' -Method Get -MaximumRedirection 0
```

Observed results:

- `GET /health/live` → HTTP `200`, `application/json`, body `{"status":"ok"}`
- `GET /health/ready` → HTTP `200`, `application/json`, body `{"status":"ready"}`
- `GET /openapi/v1.json` → HTTP `404`; source maps OpenAPI only when the ASP.NET environment is `Development`, so the safe create/read fallback was exercised.

## Sanitized create/read journey

Command shape:

```powershell
$body = '{"customerReference":"SMOKE-SPRINT2-9CFB18911104","productSku":"SKU-SMOKE","quantity":1}'
$create = Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/api/v1/orders' -Method Post -ContentType 'application/json' -Body $body
Invoke-WebRequest -UseBasicParsing -Uri "https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io$($create.Headers['Location'])" -Method Get
```

Observed results:

- POST → HTTP `201`
- Location: `/api/v1/orders/cdde2b7b-6563-4fab-8344-328223db9a1a`
- created response: synthetic identifiers preserved; quantity `1`; status `pending`; UTC timestamps present
- GET of Location → HTTP `200`
- GET response ID/customer reference/SKU/quantity matched the POST response and status remained `pending`

The request contains generated smoke-only values and no customer-sensitive data.

## Azure release identity and bootstrap exclusion

Commands:

```powershell
az containerapp show --name cloudorders-dev-api --resource-group ordersapp-development --query "{provisioningState:properties.provisioningState,runningStatus:properties.runningStatus,fqdn:properties.configuration.ingress.fqdn,latestRevisionName:properties.latestRevisionName,latestReadyRevisionName:properties.latestReadyRevisionName,traffic:properties.configuration.ingress.traffic,image:properties.template.containers[0].image,tags:tags}" -o json
az containerapp revision list --name cloudorders-dev-api --resource-group ordersapp-development --query "[].{name:name,active:properties.active,createdTime:properties.createdTime,provisioningState:properties.provisioningState,healthState:properties.healthState,runningState:properties.runningState,replicas:properties.replicas,trafficWeight:properties.trafficWeight,image:properties.template.containers[0].image}" -o json
az deployment group show --resource-group ordersapp-development --name cloudorders-development-31970305457-1 --query "{name:name,state:properties.provisioningState,timestamp:properties.timestamp,releaseId:properties.parameters.releaseId.value,containerImage:properties.parameters.containerImage.value,environmentName:properties.parameters.environmentName.value}" -o json
az acr repository show-tags --name cloudordersd583431devacr --repository cloudorders-api --detail --query "[?name=='32b5d3c82168e1d75367d4c0e6db67b1fa9b0a44'].{name:name,digest:digest,createdTime:createdTime,lastUpdateTime:lastUpdateTime}" -o json
```

Observed result:

- Container App: `Succeeded`, `Running`
- current and latest-ready revision: `cloudorders-dev-api--0000012`
- revision: active, healthy, provisioned, running, one replica, 100% traffic
- app release tag: `32b5d3c82168e1d75367d4c0e6db67b1fa9b0a44`
- deployment record: `Succeeded`, environment `development`, same release commit
- deployed image: `cloudordersd583431devacr.azurecr.io/cloudorders-api@sha256:edfcab0b51680c0698f6a3ed77921063615b0911fa7d2e92183f24cb45197700`
- ACR commit tag `32b5d3c82168e1d75367d4c0e6db67b1fa9b0a44` resolves to the same digest

This is the private, digest-pinned CloudOrders API release. It is not the public bootstrap image and the app release tag is not `bootstrap`.

## Defect: deployment smoke/summary revision race

The GitHub deployment job queried `latestReadyRevisionName` immediately after the endpoint returned HTTP 200 and recorded `cloudorders-dev-api--0000011` as the deployed revision. Azure independently shows the candidate revision is `cloudorders-dev-api--0000012`, created at `2026-08-16T20:31:31Z`, and now healthy with 100% traffic.

Evidence from the run log:

- previous ready revision before deployment: `cloudorders-dev-api--0000011`
- deployment summary `REVISION`: `cloudorders-dev-api--0000011`
- current candidate revision: `cloudorders-dev-api--0000012`

Impact: the workflow's own smoke step could accept HTTP 200 from the still-ready previous revision and publish a stale revision identity. The independent post-deployment checks above establish that the candidate revision is now healthy, but the workflow should wait for and probe/identify the expected new revision rather than treating any ingress HTTP 200 as proof of the candidate.
