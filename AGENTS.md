# Repository Guidelines

## Project Structure

`C:\\repos\\OrderApp` is the repository root. The solution is `CloudOrders.slnx`; product and namespace names are `CloudOrders`, while the intended GitHub repository name is `ordersApp`.

- `src/CloudOrders.Domain` contains order rules and status transitions.
- `src/CloudOrders.Application` contains use cases and ports.
- `src/CloudOrders.Contracts` contains versioned API and integration-event DTOs.
- `src/CloudOrders.Infrastructure` contains EF Core, SQL, and messaging adapters.
- `src/CloudOrders.Api` is the ASP.NET Core API; `src/CloudOrders.Web` will be standalone Blazor WASM.
- `src/CloudOrders.OutboxPublisher` and `src/CloudOrders.OrderProcessor` will be isolated Azure Functions.
- `tests/` contains unit, integration, end-to-end, bUnit, Playwright, and NBomber tests.
- `infra/`, `local/`, `ops/`, and `.github/workflows/` contain Bicep, local emulators, operations, and CI/CD. `infra/main.bicep` composes the focused AVM-backed modules under `infra/modules/`; pinned versions are recorded in `infra/avm-versions.md`.
- Git promotion is `feature/*` → `development` → `test` → `master`; all new feature branches must use the `feature/` prefix. Protected-branch changes require a pull request, required checks, and conversation resolution. Because this is a single-developer repository, zero independent approvals are required; the administrator remains responsible for reviewing and merging.

## Build, Test, and Development Commands

Run from the repository root:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test --configuration Release
az bicep build --file infra/main.bicep
az bicep lint --file infra/main.bicep
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
```

Local infrastructure and deployment commands are added per sprint. Never run migrations from application startup; use the documented deployment migration command. Merges to `development`, `test`, and `master` invoke the matching protected GitHub environment workflow. The MVP workflow previews and provisions Azure Container Apps, Azure Container Registry, and Log Analytics from `infra/main.bicep`, then publishes an immutable API image. AVM module versions are pinned; the registry-scoped `AcrPull` assignment remains a documented native Bicep exception because the Container App AVM role assignments are app-scoped.

## Coding Style and Naming

Target `net10.0` with stable C# 14, nullable reference types, implicit usings, analyzers, deterministic builds, and warnings treated as errors. Use four-space indentation, file-scoped namespaces, PascalCase for public types/members, and camelCase for parameters/locals. Keep API and event DTOs separate from EF/domain entities. Prefer small vertical slices and constructor-injected services.

## Testing Guidelines

Write the failing test before production code. Use xUnit for .NET tests, bUnit for components, Playwright for browser journeys/accessibility, and NBomber only for staging load tests. Test names describe behavior in PascalCase. Every sprint must leave a manual test path, automated verification, and a deployable artifact or documented infrastructure gate. Before independent review, a separate high-capability agent performs three working days of thorough developer-style local verification using technology-appropriate tests and direct state inspection (for example SQL data/migration history, outbox/idempotency rows, configuration, workflow artifacts, or authorization mappings), with sanitized evidence. After review and initial deployment to Azure `development`, a dedicated smoke-test agent validates the live release. Once the reviewed release is merged to `test`, a QA-only agent tests the feature to destruction in Azure `test`: successful, boundary, invalid, authorization, failure/recovery, state-integrity, concurrency, and regression paths appropriate to the technology. Store release IDs, commands, outcomes, defects, and re-test evidence in `docs/evidence/sprint-<number>/`; fix defects on a fresh `feature/*` branch and repeat the affected gates before promotion.

## Commits and Pull Requests

Use imperative Conventional Commit-style subjects, for example `feat: add transactional outbox`. A sprint should contain several focused commits where natural boundaries exist (tests, implementation, integration, packaging, and evidence); do not squash away useful review history during development. Pull requests must describe the sprint gate, link the relevant issue/plan, list test commands and manual evidence, call out migrations or Azure changes, and include screenshots for UI changes. Infrastructure PRs must include Bicep validation/what-if output. `.github/workflows/bicep-validation.yml` builds and lints the composition root plus all environment parameter overlays without Azure credentials. The promotion path is enforced by `.github/workflows/branch-policy.yml`; do not bypass it with direct pushes.

## Security and Decision Gates

Never commit secrets, `.env`, `local.settings.json`, auth storage state, generated ARM JSON, or real customer data. Use managed identities and least-privilege roles in Azure. The deployment workflow authenticates with GitHub OIDC; configure Azure values as GitHub environment variables/secrets, never in Bicep parameter files. Prompt the user before choosing GitHub ownership/visibility, Azure tenant/subscription/region, Entra registrations, production domains, alert owners, budgets, or production deployment approval.
