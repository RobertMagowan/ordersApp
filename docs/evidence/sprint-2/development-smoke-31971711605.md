# Sprint 2 corrected development post-deployment smoke — run 31971711605

Date: 2026-08-16 (UTC)

Status: **PASS**

Scope: independent post-deployment smoke of the corrected Sprint 2 `development` release. Actions were read-only except for two smoke-only in-memory order creates and this sanitized evidence file. No source, workflow, infrastructure, Azure resource, branch, or repository configuration changes were made.

## Release under test

- GitHub run: <https://github.com/RobertMagowan/ordersApp/actions/runs/31971711605>
- Workflow: `Deploy CloudOrders MVP`
- Branch: `development`
- Release commit: `1e6bac0420bc3e9cb956faadb69090c0184ed6b5`
- Deployment record: `cloudorders-development-31971711605-1`
- Endpoint: <https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io>
- Candidate/current revision: `cloudorders-dev-api--0000013`
- Immutable image: `cloudordersd583431devacr.azurecr.io/cloudorders-api@sha256:89da76600c28f56a4f834d5d60469ff380d4e4330c2831c54a51fd2b7c2be3d1`

Command:

```powershell
gh run view 31971711605 --repo RobertMagowan/ordersApp --json databaseId,workflowName,displayTitle,event,headBranch,headSha,status,conclusion,createdAt,startedAt,updatedAt,url,jobs
```

Observed result: run `completed/success`, created `2026-08-16T20:50:19Z` and updated `2026-08-16T20:55:55Z`. All four jobs completed successfully. In the deployment job, `Deploy immutable image after preview`, `Wait for candidate revision`, `Smoke test the deployed API`, and `Publish deployment summary` all completed successfully.

The corrected run was also the newest development deployment at the time of the final smoke gate:

```powershell
gh run list --repo RobertMagowan/ordersApp --workflow deploy.yml --branch development --limit 3 --json databaseId,headSha,status,conclusion,createdAt,updatedAt,url
```

Observed newest entry: run `31971711605`, commit `1e6bac0420bc3e9cb956faadb69090c0184ed6b5`, `completed/success`. The next entry was the earlier run `31970305457`.

## Candidate revision and workflow-summary stale-value regression

The deployment-job log was queried directly:

```powershell
gh run view 31971711605 --repo RobertMagowan/ordersApp --job 95225455310 --log |
  Select-String -Pattern 'Candidate revision cloudorders-dev-api--0000013 is the ready release',
    'CANDIDATE_REVISION: cloudorders-dev-api--0000013',
    'Candidate revision cloudorders-dev-api--0000013 .* passed ingress smoke',
    'Publish deployment summary.*REVISION: cloudorders-dev-api--0000013',
    'Publish deployment summary.*IMAGE:'
```

Observed log evidence:

```text
2026-08-16T20:55:49.4871387Z Candidate revision cloudorders-dev-api--0000013 is the ready release for cloudordersd583431devacr.azurecr.io/cloudorders-api@sha256:89da76600c28f56a4f834d5d60469ff380d4e4330c2831c54a51fd2b7c2be3d1.
2026-08-16T20:55:49.4977103Z   CANDIDATE_REVISION: cloudorders-dev-api--0000013
2026-08-16T20:55:51.2876378Z Candidate revision cloudorders-dev-api--0000013 (...) passed ingress smoke on attempt 1.
2026-08-16T20:55:51.2964726Z   IMAGE: cloudordersd583431devacr.azurecr.io/cloudorders-api@sha256:89da76600c28f56a4f834d5d60469ff380d4e4330c2831c54a51fd2b7c2be3d1
2026-08-16T20:55:51.2967526Z   REVISION: cloudorders-dev-api--0000013
```

The workflow's summary step renders `Container App revision` from that `REVISION` environment value. A fresh live query returned:

```powershell
az containerapp show --name cloudorders-dev-api --resource-group ordersapp-development `
  --query '{provisioningState:properties.provisioningState,runningStatus:properties.runningStatus,fqdn:properties.configuration.ingress.fqdn,latestRevisionName:properties.latestRevisionName,latestReadyRevisionName:properties.latestReadyRevisionName,traffic:properties.configuration.ingress.traffic,image:properties.template.containers[0].image,release:tags.release}' -o json
```

```json
{
  "latestRevisionName": "cloudorders-dev-api--0000013",
  "latestReadyRevisionName": "cloudorders-dev-api--0000013",
  "provisioningState": "Succeeded",
  "runningStatus": "Running",
  "release": "1e6bac0420bc3e9cb956faadb69090c0184ed6b5",
  "image": "cloudordersd583431devacr.azurecr.io/cloudorders-api@sha256:89da76600c28f56a4f834d5d60469ff380d4e4330c2831c54a51fd2b7c2be3d1",
  "traffic": [{ "latestRevision": true, "weight": 100 }]
}
```

Therefore the workflow-summary candidate revision is exactly the live current and latest-ready revision (`cloudorders-dev-api--0000013`), not the stale previous revision (`cloudorders-dev-api--0000012`). The stale-summary defect recorded against run `31970305457` did not reproduce.

## Exact active revision, release tag, and ACR digest

Commands:

```powershell
az containerapp revision show --name cloudorders-dev-api --resource-group ordersapp-development --revision cloudorders-dev-api--0000013 --query '{name:name,active:properties.active,createdTime:properties.createdTime,provisioningState:properties.provisioningState,healthState:properties.healthState,runningState:properties.runningState,replicas:properties.replicas,trafficWeight:properties.trafficWeight,image:properties.template.containers[0].image}' -o json

az deployment group show --resource-group ordersapp-development --name cloudorders-development-31971711605-1 --query '{name:name,state:properties.provisioningState,timestamp:properties.timestamp,releaseId:properties.parameters.releaseId.value,containerImage:properties.parameters.containerImage.value,environmentName:properties.parameters.environmentName.value}' -o json

az acr repository show --name cloudordersd583431devacr --image cloudorders-api:1e6bac0420bc3e9cb956faadb69090c0184ed6b5 --query '{tag:name,digest:digest,createdTime:createdTime,lastUpdateTime:lastUpdateTime}' -o json
```

Observed results:

- Revision `cloudorders-dev-api--0000013`: active `true`; created `2026-08-16T20:55:08Z`; `Provisioned`; `Healthy`; `Running`; one replica; traffic weight `100`.
- An independent filter over `az containerapp revision list` returned `0000013` as the only active revision.
- Deployment `cloudorders-development-31971711605-1`: `Succeeded` at `2026-08-16T20:55:22.480004Z`; environment `development`; release ID is the run SHA.
- App tag, deployment release ID, and ACR tag all equal `1e6bac0420bc3e9cb956faadb69090c0184ed6b5`.
- Workflow candidate image, live app image, active revision image, deployment parameter, and ACR tag all resolve to digest `sha256:89da76600c28f56a4f834d5d60469ff380d4e4330c2831c54a51fd2b7c2be3d1`.
- The image is private ACR content addressed by digest; neither the image nor release tag is `bootstrap`.

## TLS, liveness, and readiness

TLS was inspected through `.NET SslStream.AuthenticateAsClient()` with a validation callback that returned `true` only for `SslPolicyErrors.None`:

```powershell
$hostName = 'cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io'
$tcp = [System.Net.Sockets.TcpClient]::new($hostName, 443)
$callback = [System.Net.Security.RemoteCertificateValidationCallback]{
  param($sender, $certificate, $chain, $sslPolicyErrors)
  $script:observedErrors = $sslPolicyErrors
  return $sslPolicyErrors -eq [System.Net.Security.SslPolicyErrors]::None
}
$ssl = [System.Net.Security.SslStream]::new($tcp.GetStream(), $false, $callback)
$ssl.AuthenticateAsClient($hostName)
```

Observed at `2026-08-16T20:58:01.8364575Z`:

- protocol: `Tls13`
- certificate policy errors: `None`
- subject: `CN=mangowater-09c0bad4.ukwest.azurecontainerapps.io, O=Microsoft Corporation, L=Redmond, S=WA, C=US`
- validity: `2026-08-16T02:05:11Z` through `2027-02-12T02:05:11Z`

HTTP commands:

```powershell
Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/health/live' -Method Get -MaximumRedirection 0
Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/health/ready' -Method Get -MaximumRedirection 0
```

Observed at `2026-08-16T20:58:02Z`:

- `GET /health/live` -> HTTP `200`, `application/json; charset=utf-8`, `{"status":"ok"}`
- `GET /health/ready` -> HTTP `200`, `application/json; charset=utf-8`, `{"status":"ready"}`

## Sanitized synthetic create/read journey

Command shape:

```powershell
$body = '{"customerReference":"SMOKE-SPRINT2-2165B58B21AA","productSku":"SKU-SMOKE","quantity":1}'
$create = Invoke-WebRequest -UseBasicParsing -Uri 'https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io/api/v1/orders' -Method Post -ContentType 'application/json' -Body $body
$created = $create.Content | ConvertFrom-Json
$read = Invoke-WebRequest -UseBasicParsing -Uri "https://cloudorders-dev-api.mangowater-09c0bad4.ukwest.azurecontainerapps.io$($create.Headers['Location'])" -Method Get
$fetched = $read.Content | ConvertFrom-Json
```

Observed at `2026-08-16T20:58:37Z`:

- POST -> HTTP `201`
- Location: `/api/v1/orders/a15bf56d-336e-4dbe-9776-69f842499a36`
- GET of Location -> HTTP `200`
- created/read IDs, smoke customer reference, SKU, quantity `1`, and status `pending` matched
- `createdAt` and `updatedAt` were present, UTC, and identical between create and read responses
- all ten scripted assertions returned `true`

The request contains generated smoke-only values and no real customer data.

One earlier smoke-only order was also created while diagnosing a verification-script field-name mismatch. The script initially checked nonexistent `createdAtUtc`; direct response and contract inspection established that the API correctly returns `createdAt` and `updatedAt`. The complete journey above was then re-run with the contract-accurate assertions and passed. This was a test-harness issue, not a deployment defect.

## Defects and verdict

- Deployment/runtime defects found: **none**.
- Prior stale deployment-summary revision defect: **verified corrected**. Candidate wait, workflow smoke, summary input, live current/latest-ready identity, active revision identity, release tag, and digest all agree.
- Final verdict: **PASS** for corrected Sprint 2 `development` deployment run `31971711605`.
