# Sprint 2 rollback evidence

Status: **PROCEDURE VERIFIED LOCALLY; LIVE TEST ROLLBACK BLOCKED**

Recorded: 2026-08-16T19:01:17Z

## Retained rollback state

Before changing an existing Container App, `deploy.yml` records its image and latest ready revision. The successful summary records those values alongside the new Git SHA, unique Bicep deployment name, digest-backed image, new revision, and endpoint. First deployment records `none (first deployment)` because no prior test release exists.

The workflow deploys `cloudorders-api@sha256:...`, not the mutable commit tag. It tags the Container App with the Git release ID and uses a unique deployment-history name. An existing release is never replaced by the public bootstrap image while its successor is built.

## Operator rollback sequence

1. Select the `Rollback image` digest from the last known-good workflow summary and verify it still resolves in the environment ACR.
2. From the matching protected environment branch, run the same Bicep template with the recorded digest, port 8080, ACR identity enabled, liveness enabled, and a new auditable deployment name.
3. Wait for the new revision to become ready; require HTTP 200 from `/health/live` and `/health/ready`.
4. Verify the active image equals the selected digest and record the rollback deployment/revision in this evidence file.
5. If the failed release changed a contract or data shape, follow its sprint-specific compatibility/runbook evidence; do not assume a destructive migration reversal.

Illustrative parameter tail (values come from the protected environment and retained summary, never from committed secrets):

```powershell
az deployment group create `
  --resource-group $resourceGroup `
  --name $rollbackDeploymentName `
  --template-file infra/main.bicep `
  --parameters $parameterFile `
    releaseId=$knownGoodGitSha `
    containerImage=$knownGoodDigestImage `
    targetPort=8080 enableLivenessProbe=true useAcr=true createAcrPullRole=false
```

## Current evidence

The development baseline ACR contains the running release tag and a resolvable digest, but that release predates this candidate. `ordersapp-test` has no ACR or Container App yet, so no honest live test rollback can be executed before the required PR promotion. A first-deployment rollback drill must be performed after the first successful test release by promoting a later reviewed release and rolling back to this first retained digest.
