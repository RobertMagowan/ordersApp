# Non-production Foundation Reset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the deleted development and test MVP foundations through the protected deployment path before Sprint 3 resumes.

**Architecture:** Azure CLI creates only the two missing resource-group containers. The existing pinned AVM Bicep composition is reapplied exclusively by the repository’s protected `deploy.yml` workflow on `development` and `test`, retaining environment-scoped OIDC and immutable API image deployment. Production is excluded throughout.

**Tech Stack:** Azure CLI, GitHub Actions environments and OIDC, GitHub CLI, Bicep, Azure Container Apps, Azure Container Registry, Log Analytics.

## Global Constraints

- Target subscription: `d7dca620-095b-4dae-aa97-0d5834317f59`; region: `ukwest`.
- Create only `ordersapp-development` and `ordersapp-test`; never query-mutate or deploy to `ordersapp-production`.
- Use no direct Bicep deployment; `deploy.yml` must perform all resource deployment.
- Do not change application, Bicep, workflow, GitHub environment configuration, or secrets during the reset.
- Because deletion removes resource-group-scoped RBAC, restore the existing matching GitHub OIDC service principal's `Contributor` and `Role Based Access Control Administrator` roles at its recreated resource-group scope only; do not create Entra identities or use subscription-wide roles.
- Protected-environment approvals require the repository owner to review and approve them manually in GitHub; do not approve deployments programmatically. Record the approving actor and run IDs.
- Record sanitized run IDs, revision/digest identities, and smoke outcomes under `docs/evidence/sprint-3/` after the reset.

---

### Task 1: Recreate empty non-production resource groups

**Files:**
- Create later after live verification: `docs/evidence/sprint-3/nonproduction-foundation-reset.md`

**Interfaces:**
- Consumes: the existing `development` and `test` GitHub environment variables and OIDC secrets.
- Produces: empty `ordersapp-development` and `ordersapp-test` resource groups in `ukwest`.

- [x] **Step 1: Prove the two target groups are absent and production is outside scope**

Run:

```powershell
az group exists --name ordersapp-development
az group exists --name ordersapp-test
az group show --name ordersapp-production --query '{name:name,location:location}' --output json
```

Expected: `false`, `false`, then production identity only.

- [x] **Step 2: Create the two explicitly named containers**

Run:

```powershell
az group create --name ordersapp-development --location ukwest --tags application=CloudOrders environment=development managedBy=Bicep
az group create --name ordersapp-test --location ukwest --tags application=CloudOrders environment=test managedBy=Bicep
```

- [x] **Step 3: Verify only the intended groups changed**

Run:

```powershell
az group show --name ordersapp-development --query '{name:name,location:location,tags:tags}' --output json
az group show --name ordersapp-test --query '{name:name,location:location,tags:tags}' --output json
az resource list --resource-group ordersapp-production --output table
```

Expected: both new groups are in `ukwest` with their environment tags; production resource inventory is unchanged.

- [x] **Step 4: Restore the deleted scoped deployment roles before dispatching the workflow**

Resolve the existing service-principal object IDs and create only the two required roles at the matching resource-group scopes:

```powershell
$developmentServicePrincipalId = az ad sp show --id 50267f53-b9b7-4ec9-a6d2-ea82929b236f --query id --output tsv
$testServicePrincipalId = az ad sp show --id 366a4684-1458-4e26-b7c9-0dd547e005a2 --query id --output tsv
$developmentScope = az group show --name ordersapp-development --query id --output tsv
$testScope = az group show --name ordersapp-test --query id --output tsv
az role assignment create --assignee-object-id $developmentServicePrincipalId --assignee-principal-type ServicePrincipal --role Contributor --scope $developmentScope
az role assignment create --assignee-object-id $developmentServicePrincipalId --assignee-principal-type ServicePrincipal --role "Role Based Access Control Administrator" --scope $developmentScope
az role assignment create --assignee-object-id $testServicePrincipalId --assignee-principal-type ServicePrincipal --role Contributor --scope $testScope
az role assignment create --assignee-object-id $testServicePrincipalId --assignee-principal-type ServicePrincipal --role "Role Based Access Control Administrator" --scope $testScope
```

Verify the two expected roles for each identity with `az role assignment list --assignee-object-id <id> --scope <scope> --output table`.

### Task 2: Restore development through the protected workflow

**Files:**
- Modify: `docs/evidence/sprint-3/nonproduction-foundation-reset.md`

**Interfaces:**
- Consumes: `development` branch and its protected GitHub environment configuration.
- Produces: the AVM foundation and a healthy immutable Container App revision in `ordersapp-development`.

- [x] **Step 1: Dispatch the existing workflow from the promotion branch**

Run:

```powershell
gh workflow run deploy.yml --repo RobertMagowan/ordersApp --ref development
gh run list --repo RobertMagowan/ordersApp --workflow deploy.yml --branch development --limit 1
```

- [x] **Step 2: Owner reviews each GitHub environment approval manually in GitHub**

Review the `preview_foundation`, `prepare_release`, and `deploy_release` approvals in GitHub. Confirm each completed job summary before manually approving its successor, do not use programmatic approval, and record the authorization in the evidence.

- [x] **Step 3: Verify the resulting Azure identity and live endpoint**

Run:

```powershell
az resource list --resource-group ordersapp-development --query '[].{name:name,type:type}' --output table
az containerapp show --name cloudorders-dev-api --resource-group ordersapp-development --query '{revision:properties.latestReadyRevisionName,traffic:properties.configuration.traffic,release:tags.release,fqdn:properties.configuration.ingress.fqdn}' --output json
$fqdn = az containerapp show --name cloudorders-dev-api --resource-group ordersapp-development --query properties.configuration.ingress.fqdn --output tsv
curl.exe --fail --silent --show-error --output NUL --write-out '%{http_code}' "https://$fqdn/health/live"
```

Expected: expected AVM foundation resources, one ready healthy revision with 100% traffic, and HTTPS status `200`.

- [x] **Step 4: Record only non-sensitive evidence**

Record the GitHub run ID, Bicep deployment name, release SHA, immutable digest, revision, resource types, TLS/health result, and no secret values.

### Task 3: Restore test through the protected workflow

**Files:**
- Modify: `docs/evidence/sprint-3/nonproduction-foundation-reset.md`

**Interfaces:**
- Consumes: `test` branch and its protected GitHub environment configuration.
- Produces: the AVM foundation and a healthy immutable Container App revision in `ordersapp-test`.

- [x] **Step 1: Dispatch the existing workflow from the promotion branch**

Run:

```powershell
gh workflow run deploy.yml --repo RobertMagowan/ordersApp --ref test
gh run list --repo RobertMagowan/ordersApp --workflow deploy.yml --branch test --limit 1
```

- [x] **Step 2: Owner reviews each GitHub environment approval manually in GitHub**

Review the `preview_foundation`, `prepare_release`, and `deploy_release` approvals in GitHub. Confirm each completed job summary before manually approving its successor, do not use programmatic approval, and record the authorization in the evidence.

- [x] **Step 3: Verify the resulting Azure identity and live endpoint**

Run:

```powershell
az resource list --resource-group ordersapp-test --query '[].{name:name,type:type}' --output table
az containerapp show --name cloudorders-test-api --resource-group ordersapp-test --query '{revision:properties.latestReadyRevisionName,traffic:properties.configuration.traffic,release:tags.release,fqdn:properties.configuration.ingress.fqdn}' --output json
$fqdn = az containerapp show --name cloudorders-test-api --resource-group ordersapp-test --query properties.configuration.ingress.fqdn --output tsv
curl.exe --fail --silent --show-error --output NUL --write-out '%{http_code}' "https://$fqdn/health/live"
```

Expected: expected AVM foundation resources, one ready healthy revision with 100% traffic, and HTTPS status `200`.

- [x] **Step 4: Run final reset audit and commit evidence**

Run:

```powershell
az resource list --resource-group ordersapp-production --query '[].{name:name,type:type}' --output table
git diff --check
```

Expected: no production changes and no whitespace errors.

- [x] **Step 5: Record and commit the sanitized reset evidence**

```powershell
git add docs/evidence/sprint-3/nonproduction-foundation-reset.md
git commit -m "docs: record nonproduction foundation reset"
```

## Plan Self-Review

- **Spec coverage:** confines mutations to the two deleted resource groups; uses existing workflow/OIDC approval gates; verifies non-production resources and live health; excludes production and Sprint 3 SQL work.
- **Placeholder scan:** no unresolved execution placeholders remain; each endpoint is obtained from its Container App immediately before probing it.
- **Type consistency:** resource-group, branch, workflow, and app names match the current GitHub environment variables and Bicep parameter overlays.
