# AVM Infrastructure Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with review checkpoints.

**Goal:** Replace the monolithic native Bicep template with focused AVM-backed modules while preserving the current Azure deployment workflow and outputs.

**Architecture:** `infra/main.bicep` remains the resource-group composition root. Focused local modules compose pinned public AVM resource modules for Log Analytics, ACR, the Container Apps environment, and the Container App. The registry-scoped `AcrPull` assignment remains a documented native resource because AVM role-assignment modules do not target an individual resource scope.

**Tech Stack:** Bicep, Azure Verified Modules, Azure CLI, GitHub Actions, Azure Container Apps, Azure Container Registry, Log Analytics.

## Global Constraints

- Keep the existing `main.bicep` parameter names and four output names unchanged.
- Preserve the two-phase public-bootstrap/private-ACR deployment sequence.
- Pin AVM references to `app/container-app:0.11.0`, `app/managed-environment:0.8.1`, `container-registry/registry:0.6.0`, and `operational-insights/workspace:0.8.0`.
- Use native Bicep only for the registry-scoped `AcrPull` role assignment and document the scope limitation.
- Keep all new branches under `feature/` and use focused Conventional Commit messages.

---

### Task 1: Add AVM configuration and version manifest

**Files:**
- Create: `infra/bicepconfig.json`
- Create: `infra/avm-versions.md`

- [x] Add a `br` alias named `avm` targeting the public AVM path (`mcr.microsoft.com/bicep/avm`) and enable the Bicep linter configuration used by CI; module references will use `br/avm:res/...:<version>`.
- [x] Record each pinned module path, version, purpose, and the date/command used to validate it in `infra/avm-versions.md`.
- [x] Run `az bicep build --file infra/main.bicep` to confirm the config is discovered from the `infra` directory.
- [x] Commit: `build: configure pinned AVM modules`.

### Task 2: Create focused AVM resource modules

**Files:**
- Create: `infra/modules/observability.bicep`
- Create: `infra/modules/container-registry.bicep`
- Create: `infra/modules/container-app-environment.bicep`

- [x] Define typed parameters for names, location, tags, and the required Log Analytics/Container Apps relationships.
- [x] Use the pinned AVM modules for the workspace, registry, and managed environment.
- [x] Preserve current settings: 30-day workspace retention, resource-permission-only log access, Basic ACR SKU, admin user disabled, public registry access, and Log Analytics-backed Container Apps logs.
- [x] Expose outputs for workspace resource ID, registry resource ID/login server, and managed environment resource ID.
- [x] Build each module through the composition root and commit: `refactor: compose foundation with AVM modules`.

### Task 3: Create the Container App workload module

**Files:**
- Create: `infra/modules/container-app.bicep`
- Create or modify: `infra/modules/README.md`

- [x] Accept the existing image, ACR toggle, target port, probe toggle, replica limits, registry login server, managed environment ID, and tags.
- [x] Use `br/avm:res/app/container-app:0.11.0` with system-assigned identity, single revision mode, external HTTPS ingress, `0.25` CPU, `0.5Gi` memory, and the existing `/health/live` liveness probe.
- [x] Keep the registry configuration conditional so the public bootstrap phase does not require ACR access.
- [x] Keep `AcrPull` as the separate native registry-scoped role assignment in `main.bicep`; do not pass it as an app-scoped AVM role assignment.
- [x] Build the module and commit: `refactor: model container app with AVM`.

### Task 4: Recompose the root and add environment overlays

**Files:**
- Modify: `infra/main.bicep`
- Modify: `infra/environments/development.bicepparam`
- Create: `infra/environments/test.bicepparam`
- Create: `infra/environments/production.bicepparam`

- [x] Keep all existing root parameters and outputs, wiring module outputs to the unchanged output names.
- [x] Preserve dependency ordering: workspace/registry before managed environment, managed environment before Container App, and Container App identity before the optional role assignment.
- [x] Set all three environment overlays to `ukwest`, with the existing environment-specific names and bootstrap defaults; keep secrets and subscription-specific values out of parameter files.
- [x] Run `az bicep build --file infra/main.bicep` and `git diff --check`.
- [x] Commit: `refactor: compose environments from AVM modules`.

### Task 5: Add pull-request Bicep validation

**Files:**
- Create: `.github/workflows/bicep-validation.yml`

- [x] Trigger on pull requests that touch `infra/**/*.bicep`, `infra/**/*.bicepparam`, or `infra/bicepconfig.json`.
- [x] Install Bicep through Azure CLI, run `az bicep lint --file infra/main.bicep` and `az bicep build --file infra/main.bicep`, and fail on errors; do not require Azure credentials.
- [x] Run `az bicep build-params --file` for all three environment parameter files and remove generated build output.
- [x] Commit: `ci: validate AVM Bicep on pull requests`.

### Task 6: Update deployment verification and execute the migration

**Files:**
- Modify: `.github/workflows/deploy.yml`
- Modify: `README.md`
- Modify: `AGENTS.md`

- [x] Keep the existing workflow parameter contract and two deployment passes; update template references to use the environment overlays and pinned module composition.
- [x] Add an environment-gated `az deployment group what-if` step using each environment overlay before deployment, without exposing secrets.
- [x] Run `az bicep build --file infra/main.bicep`, `dotnet build --configuration Release`, and `dotnet test --configuration Release` locally.
- [x] Run a development resource-group `what-if`, deploy to development, and verify `/health/live` returns HTTP 200 before promoting further.
- [x] Document AVM module pins, the native ACR role-assignment exception, and manual verification evidence.
- [x] Commit: `docs: document AVM deployment verification`.

## Completion Checklist

- [x] All AVM module references are pinned and listed in `infra/avm-versions.md`.
- [x] `az bicep build --file infra/main.bicep` exits successfully.
- [x] Pull-request Bicep validation commands pass locally.
- [x] .NET build and tests pass.
- [x] Development `what-if`, deployment, and `/health/live` smoke test pass.
- [x] Working tree is clean and each implementation commit is reviewable independently.
