# Sprint 4A and 4B External ID Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Authenticate individual self-service customers with Microsoft Entra External ID, enforce ownership for every order operation, and permit one explicitly assigned existing-work-account administrator to work across customer records.

**Architecture:** The API uses the real ASP.NET Core JWT bearer handler to validate one External ID tenant, then creates or resolves a race-safe `CustomerProfile` keyed by exact issuer plus `oid`. ASP.NET authorization policies stay in the API project; application and persistence interfaces receive typed actor, target-owner, paging-boundary, and audit data rather than `ClaimsPrincipal` or cursor strings. Schema rollout is expand/migrate/contract, with a compatible API revision retained at every migration and rollback boundary.

**Tech Stack:** .NET 10/C# 14, ASP.NET Core Minimal APIs and `Microsoft.AspNetCore.Authentication.JwtBearer`, EF Core 10/Azure SQL, xUnit/Testcontainers SQL Server, Microsoft Entra External ID, MSAL.NET interactive authorization-code/PKCE, Bicep, and GitHub Actions OIDC.

## Global constraints and fixed decisions

- Work on `feature/*`; promote through pull requests `feature/*` -> `development` -> `test` -> `master`. Sprint 4A and Sprint 4B each end after Azure `test`; do not open a `test` -> `master` pull request and do not deploy production.
- A valid, verified External ID customer identity is the default product customer capability. There is no `OrderUser` app role. `user.admin` is the sole elevated product role, is case-sensitive, is a source-code constant, has `allowedMemberTypes: ["User"]`, and the API enterprise application keeps **Assignment required = No** so ordinary customers need no assignment.
- The initial product administrator is the existing work account federated into the External ID tenant and explicitly assigned `user.admin` on its External ID user object. Product roles never grant directory administration.
- The CloudOrders API registration has no Microsoft Graph permissions. The separate work-tenant federation registration may have only the documented delegated `openid`, `profile`, `email`, and `User.Read` permissions required by the Microsoft identity-provider flow; it has no Graph write or application permission.
- Request `api://{api-client-id}/Orders.Read` and `api://{api-client-id}/Orders.Write`; compare the space-delimited access-token `scp` values to bare `Orders.Read` and bare `Orders.Write` with ordinal comparison.
- Accept only access tokens with a trusted signature and lifetime, exact configured `iss`, exact `tid`, exact `aud`, an allowed `azp`, a single parseable `oid`, and a delegated `scp`. `MapInboundClaims` is `false`; code reads the raw `iss`, `tid`, `aud`, `azp`, `oid`, `scp`, `roles`, `email`, and `email_verified` names.
- Return `401` plus `WWW-Authenticate: Bearer` for a missing token, or `WWW-Authenticate: Bearer error="invalid_token"` without a diagnostic description for a malformed, expired, not-yet-valid, incorrectly signed, wrong-issuer/tenant/audience/client token or a token that cannot establish a user subject. Return `403` for an authenticated user token that lacks the route scope or carries an unsupported product role. Return the same safe `404` Problem Details shape (`errorCode=resource_not_found`, no reference or owner data) for absent and foreign resources.
- Keep `customerReference` in v1 POST/response/event contracts. It is generated and resolved by the server; the POST value is only a compatibility target selector. Ownership and idempotency use profile IDs.
- Do not call `Database.Migrate()` from API startup, grant customers role-management capability, commit secrets/real tenant IDs/real email, log tokens or claims dumps, weaken TLS, use ROPC/device code for the smoke utility, or persist browser/MSAL token state.
- Fix Critical/Important findings found before the first development merge on the current Sprint 4 feature branch. Only a defect first found after an Azure deployment uses a fresh `feature/*` remediation branch and repeats the affected gates.
- Write a failing focused test before each production change. Keep warnings at zero and record sanitized commands, release IDs, exact migration IDs, job execution names, outcomes, and re-tests under `docs/evidence/sprint-4a/` or `docs/evidence/sprint-4b/`, respectively.

---

## Exact identity configuration and claim contract

For an external tenant with subdomain `{tenant-subdomain}` and GUID `{external-tenant-id}`:

| Setting/claim | Exact meaning |
|---|---|
| `ExternalIdentity:Authority` | Metadata authority `https://{tenant-subdomain}.ciamlogin.com/{external-tenant-id}/v2.0`; this host is used to obtain OIDC metadata/signing keys. |
| `ExternalIdentity:ValidIssuer` | Exact `issuer` copied from that metadata, normally `https://{external-tenant-id}.ciamlogin.com/{external-tenant-id}/v2.0`; do not assume it equals `Authority`. |
| `ExternalIdentity:TenantId` / `tid` | The external-tenant GUID in lowercase `D` form; a workforce/home-tenant ID is invalid here. |
| `ExternalIdentity:Audience` / `aud` | Exact API application (client) ID GUID `{api-client-id}`. For a v2 access token this is deliberately not the `api://...` App ID URI used in the scope request. |
| `ExternalIdentity:AllowedClientIds` / `azp` | GUIDs of the non-secret local PKCE smoke client and, from Sprint 9 onward, the Web public client. No daemon client is allowed. |
| Requested scopes | Fully qualified `api://{api-client-id}/Orders.Read` and `api://{api-client-id}/Orders.Write`. |
| Compared `scp` values | Bare `Orders.Read` and `Orders.Write`, split on ASCII spaces and compared with `StringComparison.Ordinal`. |
| `oid` | One external-tenant user object GUID. Store `(ValidIssuer, oid)`; never key/link by email, `sub`, workforce `oid`, or display name. |
| `roles` | Empty is normal for a customer; exact `user.admin` grants cross-customer access after scope checks. Any other `user.*` value fails authorization. |
| `email` | Optional contact data only when one value is present and `email_verified` is exactly `true`; never an authorization/linking key. |

`ExternalIdentityOptions` contains `Authority`, `ValidIssuer`, `TenantId`, `Audience`, and `AllowedClientIds`. It deliberately contains no configurable admin-role or scope strings. `CloudOrdersPermissions` owns the three source constants `ReadScope = "Orders.Read"`, `WriteScope = "Orders.Write"`, and `AdminRole = "user.admin"`.

## Sprint division and phase gates

The review-safe split is by independently deployable behavior, not by the former task numbers. Sprint 4A delivers a complete authenticated ownership slice. Sprint 4B is a separate schema-and-history migration programme, with a targeted gate after every release phase.

| Sprint | Outcome | Included work | Explicitly excluded |
|---|---|---|---|
| **4A — Identity and ownership** | An authenticated customer can discover their generated reference with `GET /api/v1/me`, create/read/replay only their own orders, and a `user.admin` can access another customer's order. | Contract reconciliation; Entra control plane; real JWT validation; E1 expansion; one quiesced data transition; D1 compatibility writes; profiles; authorization/audit; `/me`; v1 POST/GET; R1; three-day development verification; test QA. | History API, cursor types/codecs, cursor secrets, rotation runbook, E2/E3, and D2. |
| **4B — History and ownership contract** | Authenticated customers can page only their own order history with rotatable, target-bound cursors; legacy idempotency storage is retired only after compatibility/soak gates. | Cursor secret/configuration; history API; R2 E2/D1; H1 history; R3 D2; 14-calendar-day rollback soak; R4 E3/D2; targeted gates plus final full assurance. | Production deployment; Graph writes; customer role management; removal of `Orders.CustomerReference` from v1. |

**Decision gates.** Before D1 receives traffic, the named data owner records `reset` or `mapped-backfill` separately for development and test. A mapped backfill needs a one-to-one protected mapping; a reset is allowed only for synthetic/disposable data. Before R4, retain D2 as a production-like rollback target for **14 calendar days** after the corresponding Azure `test` R3 smoke succeeds. Any change to that duration requires a documented plan amendment and updates the cursor-key removal date.

**Gate cadence.** Every development deployment (R1, R2, H1, R3, R4) has: focused local/TDD evidence, independent review, an Azure development smoke and exact revision/digest/migration/job inspection. Every test deployment has a targeted independent QA/rollback test. Sprint 4A then has a full three-working-day development verification and one-to-two-working-day test QA; Sprint 4B repeats those full gates after R4. A post-deployment defect uses a fresh remediation `feature/*` branch and repeats the affected gate before the next release phase.

## Release and rollback invariant

Use these releases in order; never combine the destructive boundary with an API image that still maps the removed column.

1. **E1 expansion, then controlled transition before D1 traffic:** `AddCustomerProfileOwnershipExpand` adds `CustomerProfiles`, nullable ownership columns, FKs/indexes, and retains `Orders.CustomerReference`, `IdempotencyRecords.SubjectId`, its current primary key, and all old reads/writes. The already-running Sprint 3 revision is schema-compatible only; prove that with **zero traffic** and never use it as a post-D1 rollback. Before enabling D1, remove all order traffic in the relevant non-production environment, run the approved reset/mapped-backfill transaction, and prove zero unowned orders, zero legacy idempotency rows, and no duplicate future actor/key values.
2. **D1 authenticated dual-compatible API:** D1 writes the deterministic legacy `SubjectId` value `profile:{ActorCustomerProfileId:N}` as well as nullable actor/target IDs. Until E2, the legacy `(SubjectId, IdempotencyKey)` constraint remains authoritative: the same actor/key with another target is a conflict, not a second record. D1 reads/replays only the deterministic value, returning safe `404` for a legacy null-owned order. The migration transition deleted pre-Sprint-4 idempotency rows because their actor cannot be proven. After D1 begins serving traffic, rollback is either to a recorded authenticated D1 revision or a fail-closed order-ingress outage; it is never to Sprint 3.
3. **E2 constraint transition + D1-compatible rebuild:** after D1 is the latest ready revision and a new quiescence/precondition transaction proves zero null ownership, zero legacy rows, and no duplicate `(ActorCustomerProfileId, IdempotencyKey)`, `EnforceCustomerProfileOwnership` makes order/actor/target IDs non-null, changes idempotency uniqueness to `(ActorCustomerProfileId, IdempotencyKey)`, and makes retained `SubjectId` nullable. Deploy an API rebuild with unchanged D1 persistence behavior. Rollback targets D1, never Sprint 3.
4. **H1 history after E2:** introduce the history endpoint and cursors only after the unchanged-D1 E2 release is healthy in both environments. H1 is a code/configuration release with no schema shape change and its own compatible D1 rollback target.
5. **D2 new-shape bridge:** deploy code that no longer maps, reads, or writes `SubjectId`, while E2 still retains the nullable column. D1 remains the pre-contract rollback option through the 14-calendar-day D2 soak.
6. **E3 contract + D2-compatible rebuild:** only after the D2 rollback window, `RemoveLegacyIdempotencySubject` drops `SubjectId` and the obsolete constraint/index. Deploy an API rebuild with unchanged D2 persistence behavior. Keep `Orders.CustomerReference` for v1 wire/event compatibility. A rollback after E3 selects the recorded pre-E3 D2 image/revision, never D1/Sprint 3.

Every workflow start captures the exact execution returned by `az containerapp job start --query name --output tsv` and polls only that value through `az containerapp job execution show --job-execution-name "$EXECUTION"`; listing `[0]` is forbidden because it can observe an older execution.

## Implementation inventory

### Packages and project files

| File | Change |
|---|---|
| `Directory.Packages.props` | Pin `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.11` and `Microsoft.Identity.Client` `4.73.1`. |
| `src/CloudOrders.Api/CloudOrders.Api.csproj` | Reference `Microsoft.AspNetCore.Authentication.JwtBearer`. |
| `tests/CloudOrders.UnitTests/CloudOrders.UnitTests.csproj` | Reference `src/CloudOrders.Api` so API claim, policy, audit, and cursor units are tested without moving ASP.NET types into Application. |
| `tools/CloudOrders.AuthSmoke/CloudOrders.AuthSmoke.csproj` | New `net10.0` console project referencing `Microsoft.Identity.Client`; no persistence/cache package. |
| `CloudOrders.slnx` | Add `tools/CloudOrders.AuthSmoke` under `/tools/`. |

### Product and operations files

| File(s) | Responsibility |
|---|---|
| `src/CloudOrders.Application/Identity/{AuthenticatedSubject,CustomerProfile,ICustomerProfileStore,ICustomerReferenceGenerator}.cs` | Domain-neutral subject/profile boundary and opaque reference generation. |
| `src/CloudOrders.Application/Authorization/{AuthorizationAuditEvent,IAuthorizationAuditSink}.cs` | Strongly typed, allowlisted authorization audit contract. |
| `src/CloudOrders.Application/Orders/{OwnedOrder,OrderHistoryBoundary,OrderHistorySlice}.cs` | Owner-bearing read results and typed persistence paging boundary. |
| `src/CloudOrders.Application/Abstractions/{IOrderRepository,IIdempotentOrderStore}.cs` | Owner-aware reads and actor/target idempotency. |
| `src/CloudOrders.Application/Abstractions/ISubjectIdProvider.cs`, `src/CloudOrders.Infrastructure/Identity/LocalDevelopmentSubjectIdProvider.cs` | Delete both temporary Sprint 3 subject files and all registrations/usages. |
| `src/CloudOrders.Api/Identity/{ExternalIdentityOptions,ExternalIdentityOptionsValidator,CloudOrdersPermissions,AuthenticatedSubjectReader,CurrentCustomerProfileAccessor}.cs` | Exact CIAM settings, raw-claim parsing, and request profile resolution. |
| `src/CloudOrders.Api/Identity/{ScopeRequirement,ScopeAuthorizationHandler,KnownProductRoleRequirement,KnownProductRoleAuthorizationHandler,CustomerResourceRequirement,CustomerResourceAuthorizationHandler}.cs` | All ASP.NET Core policy/resource types; none move to Application. |
| `src/CloudOrders.Api/Identity/{CloudOrdersAuthorizationResultHandler,AuthorizationAuditMetadata,LoggerAuthorizationAuditSink}.cs` | Exact 401/403 responses and allowlisted structured audit implementation. |
| `src/CloudOrders.Api/History/{OrderHistoryCursorPayload,IOrderHistoryCursorCodec,OrderHistoryCursorCodec,CursorSigningKey,ICursorSigningKeyRing,CursorSigningOptions,ConfiguredCursorSigningKeyRing}.cs` | Bounded, target-bound, authenticated cursor parsing/signing and rotation key ring. |
| `src/CloudOrders.Infrastructure/Persistence/{CustomerProfileEntity,SqlCustomerProfileStore}.cs`, `Configurations/CustomerProfileEntityConfiguration.cs` | Race-safe durable profile resolution. |
| `src/CloudOrders.Infrastructure/Persistence/{CloudOrdersDbContext,OrderEntity,IdempotencyRecordEntity,OrderPersistenceMapper,SqlOrderRepository,SqlIdempotentOrderStore}.cs` and their configuration files | Profile ownership, typed reads/history, actor/target idempotency, and phased legacy compatibility. |
| `src/CloudOrders.Contracts/Identity/CurrentCustomerResponse.cs`, `src/CloudOrders.Contracts/Orders/OrderHistoryPage.cs` | Only the `/me` and history page wire DTOs. Do not add an unused `OrderHistoryResponse`. |
| `src/CloudOrders.Api/{Program.cs,appsettings.json,appsettings.Development.json}` | Fail-closed options, real bearer pipeline, routes, policies, and secret-reference configuration names. |
| `.github/workflows/{deploy,bicep-validation}.yml`, `infra/{main.bicep,environments/*.bicepparam}`, `infra/modules/container-app.bicep` | Exact migration execution polling, non-production identity settings, cursor secret refs, and production exclusion. |
| `tools/CloudOrders.AuthSmoke/Program.cs` | Interactive system-browser PKCE token acquisition and in-memory API smoke call without token output/storage. |
| `ops/runbooks/{external-id-setup,external-id-role-operations,external-id-recovery,cursor-key-rotation,sprint-4-identity-data-transition}.md` | Tenant/federation/control-plane, role, recovery, secure key rotation, and explicit reset/backfill operations. |

### Test files

Modify the existing `tests/CloudOrders.UnitTests/{CreateOrderHandlerTests,RepositoryBootstrapTests}.cs`, `tests/CloudOrders.IntegrationTests/{ApiHealthTests,MigrationRunnerTests,OrderSqlIntegrationTests}.cs`, and `tests/CloudOrders.ArchitectureTests/{ContractPackTests,DeploymentWorkflowPolicyTests,RepositoryPolicyTests}.cs`. Create:

- `tests/CloudOrders.UnitTests/{AuthenticatedSubjectReaderTests,CustomerReferenceGeneratorTests,CustomerResourceAuthorizationHandlerTests,IdempotencyRequestHasherTests,LoggerAuthorizationAuditSinkTests,OrderHistoryCursorCodecTests}.cs`
- `tests/CloudOrders.IntegrationTests/{SignedJwtFactory,JwtBearerWebApplicationFactory,PolicyTestAuthenticationHandler,PolicyWebApplicationFactory,JwtBearerAuthenticationTests,AuthorizationPolicyIntegrationTests,CustomerProfileSqlIntegrationTests,OrderOwnershipIntegrationTests,OrderHistoryIntegrationTests,ExternalIdentityStartupTests}.cs`
- `tests/CloudOrders.ArchitectureTests/{ExternalIdentityContractTests,ExternalIdentityInfrastructureTests}.cs`

`SignedJwtFactory` signs local RSA JWTs and exercises the production `JwtBearerHandler`. `PolicyTestAuthenticationHandler` exists only in the integration-test assembly and is registered only by `PolicyWebApplicationFactory` for isolated policy/resource matrices; token-validation tests, startup tests, and deployment smoke never use it.

---

## Sprint 4A — Identity and ownership vertical slice

### Task 1: Reconcile contracts, prerequisites, data decision, and migration polling

**Files:**

- Modify: `docs/contracts/{v1-contracts,frontend-design,traceability}.md`
- Modify: `docs/superpowers/plans/{2026-08-16-cloudorders-sprint-implementation-plan,2026-08-16-sprints-2-4-execution-plan}.md`
- Modify: `.github/workflows/deploy.yml`, `tests/CloudOrders.ArchitectureTests/{ContractPackTests,DeploymentWorkflowPolicyTests}.cs`
- Create: `ops/releases/sprint-4a-e1-migration-only.json`

**Produces:** one canonical vocabulary: verified External ID customer, `user.admin`, `Orders.Read`, `Orders.Write`, `CustomerProfileId`, actor/target idempotency, and the release sequence above.

- [ ] **Step 1: Write failing contract/workflow tests.** Assert the contract and Sprint 4/Sprint 9 text contains the canonical terms and contains neither `OrderUser`, group-to-customer mapping, `CloudOrders.Orders.Read`, nor `CloudOrders.Orders.Write`. Assert the workflow assigns `EXECUTION=$(az containerapp job start ... --query name --output tsv)`, passes that exact value to `--job-execution-name`, and does not use `execution list --query '[0]'`. Assert a push merge containing the checked-in E1 manifest selects migration-only mode, runs only `AddCustomerProfileOwnershipExpand`, does not build/push/deploy an API image, and asserts the active revision/digest/traffic are unchanged; removing the manifest in the later D1 merge restores normal candidate deployment.

Run: `dotnet test tests/CloudOrders.ArchitectureTests --filter "FullyQualifiedName~ContractPackTests|FullyQualifiedName~DeploymentWorkflowPolicyTests" --configuration Release`

Expected: FAIL on the stale terminology and list-based migration polling.

- [ ] **Step 2: Update the repository contracts and roadmaps.** In contract sections 26/27/32/33, define `/me`, bare `scp` values, profile ownership, actor/target idempotency, schema expand/migrate/contract, exact 401/403/404 semantics, and `user.admin`. In Sprint 9, request the two fully qualified scope URIs and treat a verified signed-in customer as the default capability; remove the `OrderUser` role. Keep future `TestOperator` work separate.

- [ ] **Step 3: Correct migration-job observation and define merge-safe E1 mode.** Capture the start command's returned execution name, fail if empty, poll only it, report its final status/name, and include the name in the workflow summary/evidence. Create `ops/releases/sprint-4a-e1-migration-only.json` containing exactly `{ "migration": "AddCustomerProfileOwnershipExpand", "deployApi": false }`. On protected `development`/`test` push runs, `deploy.yml` reads only this exact committed manifest, validates its two-property schema and named E1 migration, skips normal artifact/API deployment, and fails if the app revision, digest, or traffic changes. The reviewed `feature/sprint4a-d1` merge deletes the manifest in the same commit that enables normal D1 deployment. Preserve the normal migration-before-candidate dependency outside that mode.

- [ ] **Step 4: Record prerequisites and transition decisions that block D1 traffic.** Before D1 can receive traffic in either environment, evidence records: external tenant/subdomain/GUID; two emergency directory admins; named Cloud Application Administrator; API and local PKCE client registrations; enabled read/write scopes; `user.admin` manifest and assignment-required setting; email-OTP user flow; work-tenant federation; the initial work account's External ID object and role assignment; protected development/test GitHub variables; the named data owner; and a per-environment `reset` or `mapped-backfill` decision. A mapped-backfill decision also records the protected mapping location, checksum, and reconciliation rule: an already-created `(issuer, oid)` profile retains its generated reference only when it equals the mapping reference; otherwise abort. The interactive `/me` smoke belongs to the D1/R1 gate after Task 5 has implemented it. Store IDs only in protected configuration/evidence, never the contract or Git.

- [ ] **Step 5: Re-run focused tests and commit.**

```powershell
dotnet test tests/CloudOrders.ArchitectureTests --filter "FullyQualifiedName~ContractPackTests|FullyQualifiedName~DeploymentWorkflowPolicyTests" --configuration Release
git add docs/contracts docs/superpowers/plans .github/workflows/deploy.yml tests/CloudOrders.ArchitectureTests
git commit -m "docs: reconcile External ID authorization contract"
```

Expected: PASS; the workflow test proves exact-execution polling.

### Task 2: Add real JWT bearer validation and separated test authentication

**Files:**

- Modify: `Directory.Packages.props`, `src/CloudOrders.Api/{CloudOrders.Api.csproj,Program.cs,appsettings.json,appsettings.Development.json}`, `tests/CloudOrders.UnitTests/CloudOrders.UnitTests.csproj`
- Create: API identity option/claim/policy files listed in the inventory
- Create: `tests/CloudOrders.UnitTests/AuthenticatedSubjectReaderTests.cs`
- Create: `tests/CloudOrders.IntegrationTests/{SignedJwtFactory,JwtBearerWebApplicationFactory,PolicyTestAuthenticationHandler,PolicyWebApplicationFactory,JwtBearerAuthenticationTests,AuthorizationPolicyIntegrationTests,ExternalIdentityStartupTests}.cs`

**Interfaces:**

```csharp
public sealed class ExternalIdentityOptions
{
    public const string SectionName = "ExternalIdentity";
    public required string Authority { get; init; }
    public required string ValidIssuer { get; init; }
    public required string TenantId { get; init; }
    public required string Audience { get; init; }
    public required string[] AllowedClientIds { get; init; }
}

public static class CloudOrdersPermissions
{
    public const string ReadScope = "Orders.Read";
    public const string WriteScope = "Orders.Write";
    public const string AdminRole = "user.admin";
}

public sealed record AuthenticatedSubject(string Issuer, Guid ObjectId, string? VerifiedContactEmail);
```

- [ ] **Step 1: Write real signed-token failures first.** Generate RSA-signed local v2 JWTs with controlled `iss`, `tid`, `aud`, `azp`, `oid`, `scp`, `roles`, `nbf`, and `exp`. `JwtBearerWebApplicationFactory` replaces remote metadata only with a static test `OpenIdConnectConfiguration` containing that RSA public key; it does not replace the authentication scheme. Cover trusted success, missing token, bad/missing signature, wrong issuer/tenant/audience/client, expired/future token, missing/multiple/malformed `oid`, missing delegated `scp`, wrong scope, unknown/case-changed role, and app-only shape.

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter FullyQualifiedName~JwtBearerAuthenticationTests --configuration Release`

Expected: FAIL because bearer authentication is not registered.

- [ ] **Step 2: Bind and validate CIAM settings.** `ExternalIdentityOptionsValidator` requires HTTPS absolute `Authority`/`ValidIssuer`, GUID tenant/audience/client IDs, exact issuer tenant segment, distinct allowed clients, and no trailing/whitespace variants. The audience is the API client-ID GUID; only requested scopes use the `api://` URI. Validate on start in Development, Test, and Production. `Testing` is not an auth bypass: each test factory must supply complete settings; only the signed-JWT factory substitutes local signing metadata.

- [ ] **Step 3: Configure the real handler.** Use `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)`, set `Authority`, `MapInboundClaims = false`, `RequireHttpsMetadata = true`, `TokenValidationParameters.ValidIssuer`, `ValidAudience`, signature/lifetime/issuer/audience validation, zero custom issuer fallback, and raw `RoleClaimType = "roles"`. In `OnTokenValidated`, reject wrong `tid`/`azp`, missing/malformed user `oid`, and app-only/no-`scp` shapes without logging claims/token. Call `UseAuthentication()` before `UseAuthorization()`; mark only live/readiness health anonymous.

- [ ] **Step 4: Implement scope/known-role policies in the API project.** `OrdersRead` requires exact `Orders.Read`; `OrdersWrite` requires exact `Orders.Write`. Empty `roles` succeeds; `user.admin` succeeds; any other `user.*` role forbids. The admin role never replaces a route scope. `CloudOrdersAuthorizationResultHandler` emits safe Problem Details and always retains a Bearer `WWW-Authenticate` header on challenges.

- [ ] **Step 5: Keep fake authentication policy-only.** Register `PolicyTestAuthenticationHandler` only inside `PolicyWebApplicationFactory.ConfigureTestServices`; add an architecture assertion that production projects contain no reference to that type/scheme. Use it for fast policy matrices only. Rework `ApiHealthTests` to supply complete Testing configuration and prove health remains anonymous while order routes challenge.

- [ ] **Step 6: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.UnitTests --filter FullyQualifiedName~AuthenticatedSubjectReaderTests --configuration Release
dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~JwtBearerAuthenticationTests|FullyQualifiedName~AuthorizationPolicyIntegrationTests|FullyQualifiedName~ExternalIdentityStartupTests|FullyQualifiedName~ApiHealthTests" --configuration Release
git add Directory.Packages.props src/CloudOrders.Api tests/CloudOrders.UnitTests tests/CloudOrders.IntegrationTests
git commit -m "feat: validate External ID bearer tokens"
```

Expected: PASS with explicit 401/403/header assertions and fail-closed startup modes.

### Task 3: Add the E1 profile schema and deterministic profile resolution

**Files:**

- Delete: `src/CloudOrders.Application/Abstractions/ISubjectIdProvider.cs`, `src/CloudOrders.Infrastructure/Identity/LocalDevelopmentSubjectIdProvider.cs`
- Create: Application profile files and Infrastructure customer-profile files listed in the inventory
- Modify: `src/CloudOrders.Infrastructure/Persistence/{CloudOrdersDbContext,OrderEntity,IdempotencyRecordEntity}.cs` and configurations
- Create: `src/CloudOrders.Infrastructure/Persistence/Migrations/*_AddCustomerProfileOwnershipExpand.{cs,Designer.cs}`; modify model snapshot
- Modify: `tests/CloudOrders.UnitTests/{CreateOrderHandlerTests,RepositoryBootstrapTests}.cs`, `tests/CloudOrders.IntegrationTests/{MigrationRunnerTests,OrderSqlIntegrationTests}.cs`
- Create: `tests/CloudOrders.UnitTests/CustomerReferenceGeneratorTests.cs`, `tests/CloudOrders.IntegrationTests/CustomerProfileSqlIntegrationTests.cs`

**Interfaces:**

```csharp
public sealed record CustomerProfile(
    Guid Id,
    string CustomerReference,
    string Issuer,
    Guid ObjectId,
    string? ContactEmail);

public interface ICustomerProfileStore
{
    Task<CustomerProfile> GetOrCreateAsync(AuthenticatedSubject subject, CancellationToken cancellationToken);
    Task<CustomerProfile?> FindByReferenceAsync(string customerReference, CancellationToken cancellationToken);
}

public interface ICustomerReferenceGenerator
{
    string Create();
}
```

- [ ] **Step 1: Write failing SQL/race/generator tests.** Use a barrier-controlled pair of contexts for simultaneous first access to one `(issuer, oid)` and assert one profile. Use a sequence generator that returns an existing reference twice then a unique reference and assert bounded collision retry. Test two `oid` values with the same verified email remain separate, unverified/missing email stores null, and later tokens never change an existing `ContactEmail`.

- [ ] **Step 2: Define the exact E1 schema.** `CustomerProfiles`: `Id uniqueidentifier`, `Issuer nvarchar(256) COLLATE Latin1_General_100_BIN2`, `ObjectId uniqueidentifier`, `CustomerReference varchar(64) COLLATE Latin1_General_100_BIN2`, `ContactEmail nvarchar(320) COLLATE Latin1_General_100_CI_AS_SC`, `CreatedAt/UpdatedAt datetimeoffset(7)`, and `RowVersion rowversion`. Name `PK_CustomerProfiles`, `AK_CustomerProfiles_Issuer_ObjectId`, and `AK_CustomerProfiles_CustomerReference`. Add nullable `Orders.CustomerProfileId`, `IdempotencyRecords.ActorCustomerProfileId`, and `TargetCustomerProfileId`, named restrictive FKs, and indexes; retain every Sprint 3 column/key in E1.

- [ ] **Step 3: Implement opaque generation and race handling.** Generate `CUS-` plus 32 uppercase hexadecimal characters from 16 cryptographically random bytes (36 allowed ASCII characters). Retry only the named customer-reference alternate-key collision up to five attempts. On the named issuer/object-ID collision, dispose the failed context, create a new context, and read the winner. After five reference collisions throw a stable internal exception; never treat an unrelated 2601/2627 as a profile race.

- [ ] **Step 4: Fix contact semantics.** On insert only, store the single syntactically valid `email` when raw `email_verified` is `true`. Existing profiles are never relinked or automatically updated/cleared from later token email; a future authenticated contact-change workflow owns that behavior. Email is neither unique nor queried for identity.

- [ ] **Step 5: Prove expand compatibility.** Upgrade a database at the Sprint 3 migration, assert old rows/keys remain usable with null new columns, and run the unmodified Sprint 3 SQL assumptions against E1. Generate/review the E1 SQL and assert it contains no drop, rename, non-null alteration, or destructive data statement.

```powershell
$env:ConnectionStrings__CloudOrders = 'Server=localhost;Database=CloudOrdersModelCheck;Integrated Security=true;Encrypt=false'
dotnet ef migrations has-pending-model-changes --project src/CloudOrders.Infrastructure --startup-project src/CloudOrders.Migrations --configuration Release
dotnet ef migrations script 20260816221235_InitialSqlPersistence AddCustomerProfileOwnershipExpand --idempotent --project src/CloudOrders.Infrastructure --startup-project src/CloudOrders.Migrations --output "$env:TEMP\cloudorders-sprint4-e1.sql"
Remove-Item Env:\ConnectionStrings__CloudOrders
```

Expected: pending-model command exits 0 with no pending changes; reviewed SQL is additive.

- [ ] **Step 6: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.UnitTests --filter FullyQualifiedName~CustomerReferenceGeneratorTests --configuration Release
dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~CustomerProfileSqlIntegrationTests|FullyQualifiedName~MigrationRunnerTests|FullyQualifiedName~OrderSqlIntegrationTests" --configuration Release
git add src/CloudOrders.Application src/CloudOrders.Infrastructure tests/CloudOrders.UnitTests tests/CloudOrders.IntegrationTests
git commit -m "feat: expand customer profile ownership schema"
```

### Task 4: Implement owner-aware authorization, actor/target idempotency, and audit

**Files:**

- Modify: `src/CloudOrders.Application/Abstractions/{IOrderRepository,IIdempotentOrderStore}.cs`
- Create: owner/audit Application files listed in the inventory
- Modify: `src/CloudOrders.Application/Orders/{CreateOrderHandler,GetOrderHandler,IdempotencyRequestHasher}.cs`
- Modify: API resource/accessor/audit files and `Program.cs`
- Modify: `src/CloudOrders.Infrastructure/Persistence/{OrderEntity,IdempotencyRecordEntity,OrderPersistenceMapper,SqlOrderRepository,SqlIdempotentOrderStore}.cs` and configurations
- Create/modify the unit and integration ownership/audit/idempotency tests listed in the inventory

**Interfaces:**

```csharp
public sealed record OrderOwner(Guid CustomerProfileId, string CustomerReference);
public sealed record OwnedOrder(Order Order, OrderOwner Owner);

public sealed record IdempotentOrderRequest(
    Guid ActorCustomerProfileId,
    Guid TargetCustomerProfileId,
    Guid IdempotencyKey,
    byte[] RequestHash,
    Order Order,
    OrderCreatedIntegrationEventV1 IntegrationEvent,
    OrderResponse Response,
    string? TraceParent);

public interface IOrderRepository
{
    Task<OwnedOrder?> GetOwnedAsync(Guid orderId, CancellationToken cancellationToken);
}

// CreateOrderHandler receives these IDs only after the API has resolved and authorized the target.
Task<CreateOrderResult> Handle(
    CreateOrderCommand command,
    Guid actorCustomerProfileId,
    Guid targetCustomerProfileId,
    Guid idempotencyKey,
    string? traceParent,
    CancellationToken cancellationToken);

public enum AuthorizationAuditAction { Authenticate, ResolveProfile, GetCurrentCustomer, CreateOrder, GetOrder }
public enum AuthorizationAuditResult { Allowed, Denied, NotFound }
public enum AuthorizationCapability { None, OrdersRead, OrdersWrite, UserAdmin }
public sealed record AuthorizationAuditEvent(
    AuthorizationAuditAction Action,
    AuthorizationAuditResult Result,
    Guid? ActorCustomerProfileId,
    Guid? TargetCustomerProfileId,
    Guid? TargetOrderId,
    AuthorizationCapability Capability,
    string TraceId,
    string Environment);
public interface IAuthorizationAuditSink
{
    ValueTask WriteAsync(AuthorizationAuditEvent auditEvent, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing Sprint 4A ownership matrix.** Cover a customer `/me` requiring `Orders.Read`, own POST requiring `Orders.Write`, own GET/replay requiring the applicable route scope, foreign POST/get safe 404, absent get safe 404, admin cross-customer, removed-admin replay with a fresh token, same idempotency key for two targets, same key for two actors, and exact safe 404 parity. Coarse missing scope is 403; invalid JWT is 401; resource denial is 404. No history endpoint or paging type exists in Sprint 4A.

- [ ] **Step 2: Resolve actor and target before mutation.** After the route policy succeeds, `CurrentCustomerProfileAccessor` reads the raw subject once per request and calls `GetOrCreateAsync`. POST resolves the normalized submitted reference to a target profile; a customer may target itself, while exact `user.admin` may target another existing profile. Missing and denied targets use one `resource_not_found` response helper.

- [ ] **Step 3: Carry owner metadata through reads.** `SqlOrderRepository.GetOwnedAsync` returns `OwnedOrder`; the endpoint authorizes `ownedOrder.Owner.CustomerProfileId` through `IAuthorizationService` before mapping the order. No handler guesses ownership from `Order.CustomerReference`, and no repository returns an ownerless order to an API read path.

- [ ] **Step 4: Bind D1 idempotency to actor and target without breaking E1.** Compute `legacySubjectId = $"profile:{actorCustomerProfileId:N}"`; use it for the retained E1 `(SubjectId, IdempotencyKey)` lookup/insert and persist nullable actor/target FKs on the same record. Hash UTF-8 `v1|{actor D lowercase}|{target D lowercase}|{canonical customerReference}|{canonical productSku}|{quantity invariant}`. A matching actor/key with another target conflicts during D1 because E1 still has the legacy key; E2 later enables the new actor/key constraint. Require target to equal `Order.CustomerProfileId`; authorize it before lookup so a newly revoked admin token cannot replay another customer's response.

- [ ] **Step 5: Implement real allowlisted audit.** `LoggerAuthorizationAuditSink` uses one structured `ILogger` event and exactly the record fields above. Authentication challenge, profile resolution, allow/deny/not-found resource decisions, and admin cross-customer actions call the abstraction. Tests capture logger state and assert there are no token, authorization-header, raw-claim, email, customer-reference, request-body, product, quantity, or response-payload keys/values.

- [ ] **Step 6: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.UnitTests --filter "FullyQualifiedName~CustomerResourceAuthorizationHandlerTests|FullyQualifiedName~IdempotencyRequestHasherTests|FullyQualifiedName~LoggerAuthorizationAuditSinkTests|FullyQualifiedName~CreateOrderHandlerTests" --configuration Release
dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~OrderOwnershipIntegrationTests|FullyQualifiedName~AuthorizationPolicyIntegrationTests|FullyQualifiedName~OrderSqlIntegrationTests" --configuration Release
git add src tests
git commit -m "feat: authorize profile-owned order operations"
```

### Task 5: Add the Sprint 4A `/me` discovery endpoint

**Files:**

- Create: `src/CloudOrders.Contracts/Identity/CurrentCustomerResponse.cs`
- Modify: `src/CloudOrders.Api/Program.cs`
- Create: `tests/CloudOrders.IntegrationTests/CurrentCustomerIntegrationTests.cs`

**Interface:**

```csharp
public sealed record CurrentCustomerResponse(string CustomerReference);
```

- [ ] **Step 1: Write failing `/me` tests.** `GET /api/v1/me` requires the exact `Orders.Read` scope, resolves exactly one authenticated profile, returns only its opaque `CustomerReference`, and remains stable across repeated calls. Cover missing/invalid token 401, missing scope 403, no email/null email, two identities with the same email, and one concurrent first `/me` plus POST sequence producing one profile/reference.

- [ ] **Step 2: Implement the narrowly scoped endpoint.** Use `CurrentCustomerProfileAccessor`, `RequireAuthorization("OrdersRead")`, and the existing profile store. Return no email, `oid`, issuer, role, order, or audit data. Do not add a list/history route, cursor type, repository list method, or cursor configuration in Sprint 4A.

- [ ] **Step 3: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~CurrentCustomerIntegrationTests|FullyQualifiedName~OrderOwnershipIntegrationTests" --configuration Release
git add src/CloudOrders.Api src/CloudOrders.Contracts tests/CloudOrders.IntegrationTests
git commit -m "feat: add current customer discovery"
```

### Task 6: Build the External ID control plane, PKCE smoke utility, and identity-only runtime configuration

**Files:**

- Modify: `CloudOrders.slnx`, `Directory.Packages.props`, `infra/{main.bicep,environments/development.bicepparam,environments/test.bicepparam,environments/production.bicepparam}`, `infra/modules/container-app.bicep`, `.github/workflows/{deploy,bicep-validation}.yml`, `README.md`, `AGENTS.md`
- Create: `tools/CloudOrders.AuthSmoke/{CloudOrders.AuthSmoke.csproj,Program.cs}`
- Create: `ops/runbooks/{external-id-setup,external-id-role-operations,external-id-recovery,sprint-4-identity-data-transition}.md`
- Create: `tests/CloudOrders.ArchitectureTests/ExternalIdentityInfrastructureTests.cs`

- [ ] **Step 1: Write failing identity infrastructure and production-exclusion tests.** Assert development/test accept protected identity identifiers, no parameter file contains a real tenant/app ID or key, `user.admin` is not a runtime setting, and production cannot enable Sprint 4 identity material. Assert all three parameter files build and the workflow never echoes secure values. Cursor keys and their runbook are Sprint 4B only.

- [ ] **Step 2: Create the External ID tenant/control plane manually and document exact owners.** Record in the setup runbook: external tenant owner/subdomain/GUID; two emergency Global Administrators; routine Cloud Application Administrator; API app owner; PKCE client owner; workforce-federation app/secret owner and expiry; user-flow owner; GitHub environment owner; cursor-key owner/rotation date; data-transition approver; and evidence location. Directory admins and the `user.admin` assignee are separate identities/capabilities.

- [ ] **Step 3: Configure API/client/user flow.** Register single-tenant API with App ID URI `api://{api-client-id}`, v2 tokens, delegated `Orders.Read`/`Orders.Write`, and `user.admin` (`User` only). Register the local native/public client with exact `http://localhost` redirect and no secret, preauthorize it for both scopes, add both apps to the email one-time-code sign-up/sign-in user flow, and set enterprise-app Assignment required to No. The API app has no Graph permission.

- [ ] **Step 4: Configure work-tenant federation.** In the work tenant create a single-tenant confidential federation app with the two exact External ID federation redirect URIs (`.../{external-tenant-id}/federation/oauth2` and `.../{tenant-subdomain}.onmicrosoft.com/federation/oauth2`), only delegated `openid profile email User.Read`, and a named/expiring secret stored only in the External ID identity-provider configuration. Add Microsoft Entra ID as an IdP with the work-tenant issuer, map verified contact claims, attach it to the user flow, sign in the one existing work account, then have the routine directory administrator assign `user.admin` to that resulting External ID user. Confirm a fresh API access token contains external `tid`/`oid`, bare scopes, and exact role; never use the workforce `oid` as the profile key.

- [ ] **Step 5: Implement safe interactive PKCE smoke.** `CloudOrders.AuthSmoke` accepts authority, public-client ID, fully qualified scope(s), and HTTPS API base URL; uses `PublicClientApplicationBuilder`, system browser, `http://localhost`, and `AcquireTokenInteractive` (authorization code + PKCE); holds the access token only in memory; calls `/api/v1/me`; prints status, `errorCode`, `traceId`, and the safe `/me` body only. It never prints/decodes the token, serializes an MSAL cache, requests Graph, disables TLS, or stores browser state. Run once as an OTP customer and once as the federated work admin, then close the process/browser session.

- [ ] **Step 6: Add fail-closed identity Bicep/workflow configuration.** Pass Authority, ValidIssuer, tenant, audience, and allowed-client IDs as protected GitHub environment variables into non-production Container App environment settings. Reject `environmentName == 'production' && externalIdentityEnabled`. Add development/test prerequisite validation before what-if; leave the production overlay disabled and identifier-free. Do not add cursor secrets or references in Sprint 4A.

- [ ] **Step 7: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.ArchitectureTests --filter FullyQualifiedName~ExternalIdentityInfrastructureTests --configuration Release
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
git add CloudOrders.slnx Directory.Packages.props tools infra .github/workflows README.md AGENTS.md ops/runbooks tests/CloudOrders.ArchitectureTests
git commit -m "feat: configure nonproduction External ID operations"
```

Expected: all commands exit 0; the production-exclusion assertions pass.

### Task 7: Execute Sprint 4A E1, data transition, D1/R1, and assurance

**Files:**

- Create/modify: `ops/runbooks/sprint-4-identity-data-transition.md`, `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`, `docs/evidence/sprint-4a/{development-verification,development-smoke,test-qa,review,data-transition}.md`
- Modify: `.github/workflows/deploy.yml` only to preserve migration-before-candidate deployment, exact execution polling, and a documented fail-closed order-ingress operation.

- [ ] **Step 1: Write failing R1 compatibility and fail-closed tests.** Extend `MigrationRunnerTests` to prove Sprint 3 starts against E1 with zero traffic, D1 works against E1, legacy null-owned orders return safe 404, and a D1 fallback is another recorded authenticated D1 revision or disabled order ingress—never Sprint 3. Assert the workflow records candidate/previous image digests, migration ID, and exact job execution.

- [ ] **Step 2: Promote the harmless E1 migration-only release through both environments.** On a reviewed `feature/sprint4a-e1` branch, add the manifest-governed protected migration-only mode from Task 1. Merge it to `development`, then merge the same E1-only `development` commit to `test` before D1 exists on `development`. In each environment run only E1 and assert the before/after API revision/digest/traffic are identical. The active Sprint 3 revision may continue normal pre-authentication traffic at this point, but record it solely as E1 schema-compatible—not as any future D1 rollback target. Do not run either data transition, expose D1, or add identity configuration yet.

- [ ] **Step 3: Quiesce, transition, and immediately deploy D1 in development.** Set order-route ingress to fail closed/remove all application traffic; wait for in-flight requests to drain; inventory profiles, orders, outbox, and idempotency; run the approved transaction. `reset` deletes in FK-safe order (`IdempotencyRecords`, `OutboxMessages`, `Orders`, then unreferenced profiles). `mapped-backfill` validates a protected one-to-one mapping, creates/reconciles profiles, sets every order owner, and deletes unverifiable pre-Sprint-4 idempotency rows. Recheck inside the transaction: zero unowned/null ownership rows, zero unmapped orders, zero legacy idempotency rows, and no duplicate future actor/key values. Immediately merge/deploy reviewed `feature/sprint4a-d1`, which deletes the E1 manifest and enables D1; restore ingress only after the D1 candidate is healthy. Never reactivate Sprint 3.

- [ ] **Step 4: Verify R1 development, then transition and promote D1 in test.** Before the three-day development verification starts, run the local interactive PKCE `/me` smoke against the local D1 API without persisting a token. Verify the deployed D1 revision/digest/TLS/health, `/me`, own POST/GET/replay, safe foreign/absent 404, admin cross-customer, role revoke/regrant with a fresh token, SQL profile/order/idempotency ownership, and redacted audit. After the development gate passes, open the normal `development` -> `test` PR for the reviewed D1 commit. During its protected test deployment, quiesce test under fail-closed ingress, run the test transition against its already-present E1 schema, deploy D1 immediately, and restore ingress only after it is healthy. Run one-to-two days of QA in test using two OTP customers plus the federated admin. No history/cursor path is asserted.

- [ ] **Step 5: Close Sprint 4A.** Fix post-deployment defects on fresh `feature/*` remediation branches and repeat the affected gate. Record immutable R1 digests/revisions, transition evidence, and release status. Do not start Sprint 4B until test QA passes and D1 is the only traffic-bearing API behavior.

## Sprint 4B — History and ownership contract

### Task 8: Execute R2 (E2 with unchanged D1 behavior)

**Files:**

- Create: `src/CloudOrders.Infrastructure/Persistence/Migrations/*_EnforceCustomerProfileOwnership.{cs,Designer.cs}` and update the snapshot.
- Modify: D1 persistence mapping/tests, `.github/workflows/deploy.yml`, and `docs/evidence/sprint-4b/r2-*.md`.

- [ ] **Step 1: Write the R2 compatibility matrix.** Test D1 against E1/E2 and the recorded immutable D1 image. Assert a fresh precondition transaction, exact migration polling, no Sprint 3 rollback, and D1 as the sole rollback target.

- [ ] **Step 2: Promote R2.** On a reviewed `feature/sprint4b-r2` branch, generate E2 only after the fresh quiesced transaction/preconditions pass; make ownership non-null, switch idempotency uniqueness to actor/key, and retain nullable `SubjectId`. Deploy/test R2 with focused migration, D1 create/get/replay, direct SQL, and rollback-to-D1 evidence. H1 starts only after R2 passes in test.

### Task 9: Add H1 history, then execute R3 and R4

**Files:**

- Create: `src/CloudOrders.Contracts/Orders/OrderHistoryPage.cs`, `src/CloudOrders.Application/Orders/{OrderHistoryBoundary,OrderHistorySlice,ListCustomerOrdersHandler}.cs`, cursor files under `src/CloudOrders.Api/History/`, `ops/runbooks/cursor-key-rotation.md`, and history tests.
- Create: `src/CloudOrders.Infrastructure/Persistence/Migrations/*_RemoveLegacyIdempotencySubject.{cs,Designer.cs}` and update the snapshot.
- Modify: `IOrderRepository`, API/SQL history persistence, non-production Bicep/workflow secret configuration, D2 mapping/tests, and `docs/evidence/sprint-4b/{h1,r3,r4}-*.md`.

- [ ] **Step 1: Write H1 history/cursor failures.** Cover exact `Orders.Read` scope, page default 20/range 1-100, `CreatedAt DESC, Id DESC`, equal-time tie breaking, no gaps/duplicates, customer target binding, 15-minute expiry, 1024-character input limit, malformed base64/JSON/signature, unknown key/version, and absent/foreign parity. Decode at the API/handler, pass only `OrderHistoryBoundary?`, and query only the authorized profile with `pageSize + 1`.

- [ ] **Step 2: Implement and promote H1 only after R2 test passes.** Deterministically sign a target-bound payload with HMAC-SHA256; require known key/version/profile/expiry and a 32-byte minimum key. Pass current/previous material only as non-production `@secure()` Bicep parameters into Container App secrets and `secretRef`; Phase A distributes K2 validation-only, B signs K2 and validates K1, and C removes K1 only after cursor TTL plus the 14-day D2 window. Review, smoke, and QA H1 against E2; preserve D1 as rollback target.

- [ ] **Step 3: Promote R3 (D2, no migration) and soak.** On a reviewed `feature/sprint4b-r3` branch remove `SubjectId` from D2 code while E2 retains it; deploy/test with targeted smoke, direct SQL, and D2-to-D1 rollback proof. Retain exact D1/D2 images and D2 as the ready rollback target for 14 calendar days after test smoke.

- [ ] **Step 4: Promote R4 (E3 + D2-compatible rebuild).** Only after the soak, use a reviewed `feature/sprint4b-r4` branch to drop the legacy column/key, deploy/test D2-compatible code, and prove rollback only to the retained pre-E3 D2 image. Record that D1 and Sprint 3 are permanently unavailable as post-E3 rollbacks.

### Task 10: Run final verification and close Sprint 4B without production

**Files:**

- Create: `docs/evidence/sprint-4b/{development-verification,development-smoke,test-qa,review}.md`
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md` only after all gates pass.

- [ ] **Step 1: Run repository, migration, and direct-SQL gates.** Run restore, format, release build/test, Bicep lint/build/all parameter builds, `git diff --check`, `dotnet ef migrations has-pending-model-changes`, idempotent E1/E2/E3 scripts, and direct `__EFMigrationsHistory`/constraints/FKs/indexes/columns inspection. Assert required ownership at E2, history index ordering at H1, and `SubjectId` absent only at E3.

- [ ] **Step 2: Run independent review and targeted release checks.** Review code, contracts, migrations, workflow/Bicep, runbooks, audit redaction, secret handling, release digests, and prior-image compatibility. Development and test each receive a targeted independent smoke/QA gate for R2, H1, R3, and R4 before the next phase; fix post-deployment defects on fresh branches.

- [ ] **Step 3: Repeat full assurance after R4.** Complete three working days of developer-style local verification and one-to-two working days of independent Azure test QA. Cover cryptographic/claim negatives locally with signed JWTs; use only live-realizable customer/admin/sign-out/role-revoke/role-regrant, history, cursor, concurrency, recovery, SQL-integrity, and rollback states in Azure. Store sanitized evidence and update Sprint status. Do not deploy production.

## Plan self-review checklist

- **Coverage:** Tasks 1-10 cover every token, ownership, migration/rollback, cursor, audit, control-plane, federation, runtime, smoke, and QA requirement while retaining default customers, one initial federated admin, no Graph writes for the API, and no production.
- **Terminology:** The only customer capability term is verified External ID customer; the only elevated product role is `user.admin`; requested scope URIs and bare `scp` comparisons are intentionally distinct.
- **Type consistency:** `AuthenticatedSubject -> CustomerProfile`; `OwnedOrder.Owner.CustomerProfileId` is the read authorization resource; idempotency uses actor and target profile IDs; persistence receives `OrderHistoryBoundary`, while only the API cursor codec handles `OrderHistoryCursorPayload`/strings.
- **File consistency:** ASP.NET policy types remain under `CloudOrders.Api/Identity`; temporary subject-provider files are deleted; `OrderHistoryPage` is the sole history response DTO.
- **Placeholder scan:** implementation values are derived through named tenant/app configuration and protected evidence; the plan contains no unfinished implementation instruction. Before execution, search case-insensitively for placeholder markers and inconsistent type/property names, then correct any result.
