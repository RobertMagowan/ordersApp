# Pinned Azure Verified Modules

These module versions are pinned deliberately. Upgrade them as a reviewed infrastructure change and validate the generated template and Azure `what-if` output before promotion.

| Module | Version | Purpose |
| --- | --- | --- |
| `br/avm:res/app/container-app` | `0.11.0` | CloudOrders API Container App |
| `br/avm:res/app/managed-environment` | `0.8.1` | Container Apps managed environment |
| `br/avm:res/container-registry/registry` | `0.6.0` | Private image registry |
| `br/avm:res/operational-insights/workspace` | `0.8.0` | Log Analytics workspace |

Validation baseline: Azure CLI Bicep build/lint from the repository root with `az bicep build --file infra/main.bicep` and `az bicep lint --file infra/main.bicep`.
