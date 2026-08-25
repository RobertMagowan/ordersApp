# Non-production Foundation Reset Evidence

**Date:** 2026-08-25
**Scope:** Recreate only the intentionally deleted development and test MVP foundations in UK West before Sprint 3 resumes.
**Production:** `ordersapp-production` was never mutated; independent read-only inventories were empty.

## Reset actions

The empty resource groups were recreated with the repository tags:

| Resource group | Region | Tags |
| --- | --- | --- |
| `ordersapp-development` | `ukwest` | `application=CloudOrders`, `environment=development`, `managedBy=Bicep` |
| `ordersapp-test` | `ukwest` | `application=CloudOrders`, `environment=test`, `managedBy=Bicep` |

Deleting a resource group also removed its scoped OIDC role assignments. With explicit owner approval, the existing GitHub deployment service principals regained only their prior role model, scoped to their matching group:

| Identity | Scope | Roles |
| --- | --- | --- |
| `ordersApp-github-actions-development` | `ordersapp-development` | `Contributor`; `Role Based Access Control Administrator` |
| `ordersApp-github-actions-test` | `ordersapp-test` | `Contributor`; `Role Based Access Control Administrator` |

No Entra application, federated credential, GitHub secret, GitHub environment setting, Bicep source, or application source was changed.

## Protected workflow evidence

| Environment | Workflow run | Ref / release SHA | Result |
| --- | --- | --- | --- |
| Development | [32740422826](https://github.com/RobertMagowan/ordersApp/actions/runs/32740422826) | `development` / `50d41ab3fc49f9e04d9bfa943f693bcfa58a8a9e` | All four jobs succeeded after the scoped RBAC restoration. |
| Test | [32823335502](https://github.com/RobertMagowan/ordersApp/actions/runs/32823335502) | `test` / `29034e731278e55a625960d39502929db4983a85` | All four jobs succeeded. |

Each run used the existing GitHub OIDC identity and the protected `preview_foundation`, `prepare_release`, and `deploy_release` approvals. The repository owner explicitly authorized each approval after its predecessor completed; no direct Bicep deployment was used.

## Independent live verification

Each non-production group contains exactly the four expected foundation resources: Azure Container Registry, Log Analytics workspace, Container Apps managed environment, and API Container App.

| Environment | Ready revision | Immutable API image | Result |
| --- | --- | --- | --- |
| Development | `cloudorders-dev-api--0000001` | `cloudordersd583431devacr.azurecr.io/cloudorders-api@sha256:c70713886ebced58449be392042ded1af0d96750eeb7f8b46c06d1d6fb2b3c6a` | Latest and latest-ready match; one active healthy/provisioned/running replica has 100% traffic; release tag and ACR SHA tag resolve to the same digest; HTTPS `/health/live` returned 200 with normal certificate validation. |
| Test | `cloudorders-test-api--0000001` | `cloudorderst583431testacr.azurecr.io/cloudorders-api@sha256:6cdfc8e67731bb01882a0d040e598368096c7f4b92f711efbd3fd3bbe853ade1` | Latest and latest-ready match; one active healthy/provisioned/running replica has 100% traffic; release tag and ACR SHA tag resolve to the same digest; HTTPS `/health/live` returned 200 with normal certificate validation. |

The test verifier recorded 19/19 checks passing. This is foundation verification only: it does not replace Sprint 3 SQL development verification or test-environment QA.
