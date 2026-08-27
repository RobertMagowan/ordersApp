# Sprint 4 External ID Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Authenticate individual self-service customers with Microsoft Entra External ID, enforce ownership for every order operation, and permit one explicitly assigned existing-work-account administrator to work across customer records.

**Architecture:** The API uses the real ASP.NET Core JWT bearer handler to validate one External ID tenant, then creates or resolves a race-safe `CustomerProfile` keyed by exact issuer plus `oid`. ASP.NET authorization policies stay in the API project; application and persistence interfaces receive typed actor, target-owner, paging-boundary, and audit data rather than `ClaimsPrincipal` or cursor strings. Schema rollout is expand/migrate/contract, with a compatible API revision retained at every migration and rollback boundary.

**Tech Stack:** .NET 10/C# 14, ASP.NET Core Minimal APIs and `Microsoft.AspNetCore.Authentication.JwtBearer`, EF Core 10/Azure SQL, xUnit/Testcontainers SQL Server, Microsoft Entra External ID, MSAL.NET interactive authorization-code/PKCE, Bicep, and GitHub Actions OIDC.

## Global constraints and fixed decisions

- Work on `feature/*`; promote through pull requests `feature/*` -> `development` -> `test` -> `master`. Sprint 4 ends after Azure `test`; do not open a `test` -> `master` pull request and do not deploy production.
- A valid, verified External ID customer identity is the default product customer capability. There is no `OrderUser` app role. `user.admin` is the sole elevated product role, is case-sensitive, is a source-code constant, has `allowedMemberTypes: ["User"]`, and the API enterprise application keeps **Assignment required = No** so ordinary customers need no assignment.
- The initial product administrator is the existing work account federated into the External ID tenant and explicitly assigned `user.admin` on its External ID user object. Product roles never grant directory administration.
- The CloudOrders API registration has no Microsoft Graph permissions. The separate work-tenant federation registration may have only the documented delegated `openid`, `profile`, `email`, and `User.Read` permissions required by the Microsoft identity-provider flow; it has no Graph write or application permission.
- Request `api://{api-client-id}/Orders.Read` and `api://{api-client-id}/Orders.Write`; compare the space-delimited access-token `scp` values to bare `Orders.Read` and bare `Orders.Write` with ordinal comparison.
- Accept only access tokens with a trusted signature and lifetime, exact configured `iss`, exact `tid`, exact `aud`, an allowed `azp`, a single parseable `oid`, and a delegated `scp`. `MapInboundClaims` is `false`; code reads the raw `iss`, `tid`, `aud`, `azp`, `oid`, `scp`, `roles`, `email`, and `email_verified` names.
- Return `401` plus `WWW-Authenticate: Bearer` for a missing token, or `WWW-Authenticate: Bearer error="invalid_token"` without a diagnostic description for a malformed, expired, not-yet-valid, incorrectly signed, wrong-issuer/tenant/audience/client token or a token that cannot establish a user subject. Return `403` for an authenticated user token that lacks the route scope or carries an unsupported product role. Return the same safe `404` Problem Details shape (`errorCode=resource_not_found`, no reference or owner data) for absent and foreign resources.
- Keep `customerReference` in v1 POST/response/event contracts. It is generated and resolved by the server; the POST value is only a compatibility target selector. Ownership and idempotency use profile IDs.
- Do not call `Database.Migrate()` from API startup, grant customers role-management capability, commit secrets/real tenant IDs/real email, log tokens or claims dumps, weaken TLS, use ROPC/device code for the smoke utility, or persist browser/MSAL token state.
- Fix Critical/Important findings found before the first development merge on the current Sprint 4 feature branch. Only a defect first found after an Azure deployment uses a fresh `feature/*` remediation branch and repeats the affected gates.
- Write a failing focused test before each production change. Keep warnings at zero and record sanitized commands, release IDs, exact migration IDs, job execution names, outcomes, and re-tests under `docs/evidence/sprint-4/`.

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

## Release and rollback invariant

Use these releases in order; never combine the destructive boundary with an API image that still maps the removed column.

1. **E1 expand + D1 dual-compatible API:** `AddCustomerProfileOwnershipExpand` adds `CustomerProfiles`, nullable ownership columns, FKs/indexes, and retains `Orders.CustomerReference`, `IdempotencyRecords.SubjectId`, its current primary key, and all old reads/writes. The already-running Sprint 3 revision remains schema-compatible. D1 writes both `SubjectId` and the new actor/target IDs and recognizes both old and future idempotency constraint names.
2. **Controlled non-production data transition:** inventory each environment, then obtain the named data owner's explicit `reset` or `mapped-backfill` decision. Reset is allowed only when every row is synthetic/disposable. Backfill uses an approved, one-to-one `CustomerReference -> (ValidIssuer, oid)` mapping held outside Git; never infer from email/reference. Pre-Sprint-4 idempotency rows are deleted after evidence because their actor cannot be proven. The gate requires zero unmapped orders and zero legacy idempotency rows.
3. **E2 constraint transition + D1-compatible rebuild:** after D1 is the latest ready revision, `EnforceCustomerProfileOwnership` makes order/actor/target IDs non-null, changes idempotency uniqueness to `(ActorCustomerProfileId, IdempotencyKey)`, and makes retained `SubjectId` nullable. Deploy an API rebuild with unchanged D1 persistence behavior; its digest changes because the referenced Infrastructure assembly contains E2, but both previous and candidate D1-compatible revisions support E2. Rollback targets D1, never the original Sprint 3 image.
4. **D2 new-shape bridge:** deploy code that no longer maps, reads, or writes `SubjectId`, while E2 still retains the nullable column. D1 remains a pre-contract rollback option.
5. **E3 contract + D2-compatible rebuild:** after the documented D2 rollback window, `RemoveLegacyIdempotencySubject` drops `SubjectId` and the obsolete constraint/index only. Deploy an API rebuild with unchanged D2 persistence behavior; its digest changes because the referenced Infrastructure assembly contains E3, but the previous ready D2 revision remains compatible. Keep `Orders.CustomerReference` for v1 wire/event compatibility. A rollback after E3 selects the recorded pre-E3 D2 image/revision, never D1/Sprint 3.

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

### Task 1: Reconcile contracts, terminology, prerequisites, and migration polling

**Files:**

- Modify: `docs/contracts/{v1-contracts,frontend-design,traceability}.md`
- Modify: `docs/superpowers/plans/{2026-08-16-cloudorders-sprint-implementation-plan,2026-08-16-sprints-2-4-execution-plan}.md`
- Modify: `.github/workflows/deploy.yml`, `tests/CloudOrders.ArchitectureTests/{ContractPackTests,DeploymentWorkflowPolicyTests}.cs`

**Produces:** one canonical vocabulary: verified External ID customer, `user.admin`, `Orders.Read`, `Orders.Write`, `CustomerProfileId`, actor/target idempotency, and the release sequence above.

- [ ] **Step 1: Write failing contract/workflow tests.** Assert the contract and Sprint 4/Sprint 9 text contains the canonical terms and contains neither `OrderUser`, group-to-customer mapping, `CloudOrders.Orders.Read`, nor `CloudOrders.Orders.Write`. Assert the workflow assigns `EXECUTION=$(az containerapp job start ... --query name --output tsv)`, passes that exact value to `--job-execution-name`, and does not use `execution list --query '[0]'`.

Run: `dotnet test tests/CloudOrders.ArchitectureTests --filter "FullyQualifiedName~ContractPackTests|FullyQualifiedName~DeploymentWorkflowPolicyTests" --configuration Release`

Expected: FAIL on the stale terminology and list-based migration polling.

- [ ] **Step 2: Update the repository contracts and roadmaps.** In contract sections 26/27/32/33, define `/me`, bare `scp` values, profile ownership, actor/target idempotency, schema expand/migrate/contract, exact 401/403/404 semantics, and `user.admin`. In Sprint 9, request the two fully qualified scope URIs and treat a verified signed-in customer as the default capability; remove the `OrderUser` role. Keep future `TestOperator` work separate.

- [ ] **Step 3: Correct migration-job observation.** Capture the start command's returned execution name, fail if empty, poll only it, report its final status/name, and include the name in the workflow summary/evidence. Preserve the existing migration-before-candidate dependency.

- [ ] **Step 4: Record prerequisites that block the first development merge.** The Sprint 4 PR cannot merge until the runbook evidence records: external tenant/subdomain/GUID; two emergency directory admins; named Cloud Application Administrator; API and local PKCE client registrations; enabled read/write scopes; `user.admin` manifest and assignment-required setting; email-OTP user flow; work-tenant federation; the initial work account's External ID object and role assignment; protected development GitHub variables/secrets; cursor-key owner/rotation date; development data-transition decision; and a successful local interactive `/me` smoke. Store IDs only in protected configuration/evidence, never the contract or Git.

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
- Create: owner/history/audit Application files listed in the inventory
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
    Task<OrderHistorySlice> ListOwnedAsync(
        Guid customerProfileId,
        OrderHistoryBoundary? before,
        int pageSize,
        CancellationToken cancellationToken);
}

// CreateOrderHandler receives these IDs only after the API has resolved and authorized the target.
Task<CreateOrderResult> Handle(
    CreateOrderCommand command,
    Guid actorCustomerProfileId,
    Guid targetCustomerProfileId,
    Guid idempotencyKey,
    string? traceParent,
    CancellationToken cancellationToken);

public enum AuthorizationAuditAction { Authenticate, ResolveProfile, GetCurrentCustomer, CreateOrder, GetOrder, ListCustomerOrders }
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

- [ ] **Step 1: Write the failing ownership matrix.** Cover customer `/me`, own POST/get, foreign POST/get, absent get, admin cross-customer, removed-admin replay with a fresh token, same idempotency key for two targets, same key for two actors, and exact safe 404 parity. Coarse missing scope is 403; invalid JWT is 401; resource denial is 404.

- [ ] **Step 2: Resolve actor and target before mutation.** After the route policy succeeds, `CurrentCustomerProfileAccessor` reads the raw subject once per request and calls `GetOrCreateAsync`. POST resolves the normalized submitted reference to a target profile; a customer may target itself, while exact `user.admin` may target another existing profile. Missing and denied targets use one `resource_not_found` response helper.

- [ ] **Step 3: Carry owner metadata through reads.** `SqlOrderRepository.GetOwnedAsync` returns `OwnedOrder`; the endpoint authorizes `ownedOrder.Owner.CustomerProfileId` through `IAuthorizationService` before mapping the order. No handler guesses ownership from `Order.CustomerReference`, and no repository returns an ownerless order to an API read path.

- [ ] **Step 4: Bind idempotency to actor and target.** Scope records by `(ActorCustomerProfileId, IdempotencyKey)`. Hash UTF-8 `v1|{actor D lowercase}|{target D lowercase}|{canonical customerReference}|{canonical productSku}|{quantity invariant}`. Persist both actor and target FKs; require target to equal `Order.CustomerProfileId`. Authorize the target before exact replay lookup so a role revoked in a newly issued token cannot replay another customer's response.

- [ ] **Step 5: Implement real allowlisted audit.** `LoggerAuthorizationAuditSink` uses one structured `ILogger` event and exactly the record fields above. Authentication challenge, profile resolution, allow/deny/not-found resource decisions, and admin cross-customer actions call the abstraction. Tests capture logger state and assert there are no token, authorization-header, raw-claim, email, customer-reference, request-body, product, quantity, or response-payload keys/values.

- [ ] **Step 6: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.UnitTests --filter "FullyQualifiedName~CustomerResourceAuthorizationHandlerTests|FullyQualifiedName~IdempotencyRequestHasherTests|FullyQualifiedName~LoggerAuthorizationAuditSinkTests|FullyQualifiedName~CreateOrderHandlerTests" --configuration Release
dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~OrderOwnershipIntegrationTests|FullyQualifiedName~AuthorizationPolicyIntegrationTests|FullyQualifiedName~OrderSqlIntegrationTests" --configuration Release
git add src tests
git commit -m "feat: authorize profile-owned order operations"
```

### Task 5: Add `/me`, typed history paging, and securely rotatable cursors

**Files:**

- Create: `src/CloudOrders.Contracts/Identity/CurrentCustomerResponse.cs`, `src/CloudOrders.Contracts/Orders/OrderHistoryPage.cs`
- Create: `src/CloudOrders.Application/Orders/{OrderHistoryBoundary,OrderHistorySlice,ListCustomerOrdersHandler}.cs`
- Create: cursor files under `src/CloudOrders.Api/History/`
- Modify: `src/CloudOrders.Api/Program.cs`, `src/CloudOrders.Infrastructure/Persistence/SqlOrderRepository.cs`
- Create: `tests/CloudOrders.UnitTests/OrderHistoryCursorCodecTests.cs`, `tests/CloudOrders.IntegrationTests/OrderHistoryIntegrationTests.cs`

**Interfaces and payload:**

```csharp
public sealed record CurrentCustomerResponse(string CustomerReference);
public sealed record OrderHistoryPage(IReadOnlyList<OrderResponse> Items, string? NextCursor);
public sealed record OrderHistoryBoundary(DateTimeOffset CreatedAtUtc, Guid OrderId);
public sealed record OrderHistorySlice(IReadOnlyList<OwnedOrder> Items, OrderHistoryBoundary? NextBoundary);
public sealed record OrderHistoryCursorPayload(
    int Version,
    string KeyId,
    Guid CustomerProfileId,
    long CreatedAtUtcTicks,
    Guid OrderId,
    long ExpiresAtUnixSeconds);

public sealed record CursorSigningKey(string Id, ReadOnlyMemory<byte> Material);

public interface ICursorSigningKeyRing
{
    CursorSigningKey SigningKey { get; }
    bool TryGetValidationKey(string keyId, out CursorSigningKey key);
}
```

- [ ] **Step 1: Write failing `/me`/history/cursor tests.** Cover default page 20, range 1-100, `CreatedAt DESC, Id DESC`, equal-timestamp tie breaking, no duplicates/gaps, target binding, 15-minute expiry, 1024-character input limit, malformed base64/JSON/signature, fixed-time signature rejection, unknown key, unsupported version, and absent/foreign identical 404.

- [ ] **Step 2: Keep cursor strings above persistence.** Decode in the API/handler, pass only `OrderHistoryBoundary?` to the repository, fetch `pageSize + 1`, and encode the returned typed boundary. The repository query filters the authorized `CustomerProfileId` and then applies the exact tuple boundary. It never parses, signs, or returns a cursor string.

- [ ] **Step 3: Sign a target-bound payload.** Serialize the six fixed properties in a deterministic order, base64url-encode UTF-8 payload, and append base64url HMAC-SHA256 over that encoded payload. Require version `1`, known `KeyId`, matching target profile, exact UTC ticks, positive expiry, and a 32-byte-or-longer decoded key. All parse/authentication failures return the same `400 invalid_cursor` without revealing which check failed.

- [ ] **Step 4: Implement rollback-safe three-phase rotation.** The key ring has one signing key and at most one additional validation key. Phase A deploys K2 as validation-only while K1 still signs; Phase B deploys K2 current/K1 previous, so the prior revision already validates K2; Phase C removes K1 only after cursor TTL plus the documented rollback window. Tests instantiate the Phase A/B rings and prove K1 cursor -> B, K2 cursor -> A, expiry, then removal after overlap.

- [ ] **Step 5: Verify and commit.**

```powershell
dotnet test tests/CloudOrders.UnitTests --filter FullyQualifiedName~OrderHistoryCursorCodecTests --configuration Release
dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~OrderHistoryIntegrationTests|FullyQualifiedName~OrderOwnershipIntegrationTests" --configuration Release
git add src tests
git commit -m "feat: add authorized customer order history"
```

### Task 6: Build the External ID control plane, PKCE smoke utility, and non-production runtime configuration

**Files:**

- Modify: `CloudOrders.slnx`, `Directory.Packages.props`, `infra/{main.bicep,environments/development.bicepparam,environments/test.bicepparam,environments/production.bicepparam}`, `infra/modules/container-app.bicep`, `.github/workflows/{deploy,bicep-validation}.yml`, `README.md`, `AGENTS.md`
- Create: `tools/CloudOrders.AuthSmoke/{CloudOrders.AuthSmoke.csproj,Program.cs}`
- Create: the five `ops/runbooks/*.md` files listed in the inventory
- Create: `tests/CloudOrders.ArchitectureTests/ExternalIdentityInfrastructureTests.cs`

- [ ] **Step 1: Write failing infrastructure and production-exclusion tests.** Assert development/test accept protected identifiers and secret references, no parameter file contains a real tenant/app ID or key, `user.admin` is not a runtime setting, and production cannot enable Sprint 4 identity/cursor material. Assert all three parameter files build and the workflow never echoes secure values.

- [ ] **Step 2: Create the External ID tenant/control plane manually and document exact owners.** Record in the setup runbook: external tenant owner/subdomain/GUID; two emergency Global Administrators; routine Cloud Application Administrator; API app owner; PKCE client owner; workforce-federation app/secret owner and expiry; user-flow owner; GitHub environment owner; cursor-key owner/rotation date; data-transition approver; and evidence location. Directory admins and the `user.admin` assignee are separate identities/capabilities.

- [ ] **Step 3: Configure API/client/user flow.** Register single-tenant API with App ID URI `api://{api-client-id}`, v2 tokens, delegated `Orders.Read`/`Orders.Write`, and `user.admin` (`User` only). Register the local native/public client with exact `http://localhost` redirect and no secret, preauthorize it for both scopes, add both apps to the email one-time-code sign-up/sign-in user flow, and set enterprise-app Assignment required to No. The API app has no Graph permission.

- [ ] **Step 4: Configure work-tenant federation.** In the work tenant create a single-tenant confidential federation app with the two exact External ID federation redirect URIs (`.../{external-tenant-id}/federation/oauth2` and `.../{tenant-subdomain}.onmicrosoft.com/federation/oauth2`), only delegated `openid profile email User.Read`, and a named/expiring secret stored only in the External ID identity-provider configuration. Add Microsoft Entra ID as an IdP with the work-tenant issuer, map verified contact claims, attach it to the user flow, sign in the one existing work account, then have the routine directory administrator assign `user.admin` to that resulting External ID user. Confirm a fresh API access token contains external `tid`/`oid`, bare scopes, and exact role; never use the workforce `oid` as the profile key.

- [ ] **Step 5: Implement safe interactive PKCE smoke.** `CloudOrders.AuthSmoke` accepts authority, public-client ID, fully qualified scope(s), and HTTPS API base URL; uses `PublicClientApplicationBuilder`, system browser, `http://localhost`, and `AcquireTokenInteractive` (authorization code + PKCE); holds the access token only in memory; calls `/api/v1/me`; prints status, `errorCode`, `traceId`, and the safe `/me` body only. It never prints/decodes the token, serializes an MSAL cache, requests Graph, disables TLS, or stores browser state. Run once as an OTP customer and once as the federated work admin, then close the process/browser session.

- [ ] **Step 6: Add fail-closed Bicep/workflow configuration.** Pass Authority, ValidIssuer, tenant, audience, and allowed-client IDs as protected GitHub environment variables into non-production Container App env settings. Pass current/previous cursor material only through `@secure()` Bicep parameters into Container App secrets and use `secretRef`; keep key IDs non-secret. Reject `environmentName == 'production' && externalIdentityEnabled`. Add dev/test prerequisite validation before what-if; leave production overlay disabled and identifier-free.

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

### Task 7: Promote E1/D1 -> E2/D1 -> D2 -> E3/D2 in lockstep

**Files:**

- Create: `src/CloudOrders.Infrastructure/Persistence/Migrations/*_EnforceCustomerProfileOwnership.{cs,Designer.cs}`, `*_RemoveLegacyIdempotencySubject.{cs,Designer.cs}`; update snapshot at each boundary
- Modify: D1 then D2 versions of persistence files in the inventory and `.github/workflows/deploy.yml`
- Create/modify: `ops/runbooks/sprint-4-identity-data-transition.md`, `tests/CloudOrders.IntegrationTests/MigrationRunnerTests.cs`, `docs/evidence/sprint-4/development-verification.md`

- [ ] **Step 1: Write the failing compatibility matrix before any phased release.** Extend `MigrationRunnerTests` to exercise Sprint 3 against E1, D1 against E1/E2, and D2 against E2/E3. Add workflow-policy assertions that each phase records the prior compatible revision and exact migration execution. Run the focused tests and observe failures before creating E2/E3 or deleting the D1 legacy mapping.

- [ ] **Step 2: Promote R1 (E1 + D1) through both environments before advancing development.** Merge the reviewed current Sprint 4 feature branch to development, capture previous ready revision/image, exact E1 migration ID and job execution, candidate revision/image, and smoke, and prove the previous Sprint 3 image starts against E1 before allowing D1 traffic. Promote that same R1 commit/artifacts to test and repeat the compatibility proof. Do not begin the E2 phase while either environment is still on Sprint 3.

- [ ] **Step 3: Inventory and choose exactly one data mode per environment.** Record counts and references for profiles, orders, outbox, and idempotency separately in development and test. `reset` requires written confirmation that all rows are synthetic/disposable and deletes in FK-safe order (`IdempotencyRecords`, `OutboxMessages`, `Orders`, then unreferenced profiles). `mapped-backfill` imports a secure uncommitted mapping, rejects duplicate issuer/oid/reference or incomplete coverage, creates profiles, sets every order owner, and deletes unverifiable legacy idempotency rows. Both modes run in a transaction, print counts not emails/IDs, and abort on any unmapped order. Complete and evidence both environments before R2.

- [ ] **Step 4: Promote R2 (E2 + D1-compatible behavior) through both environments.** On the current R2 feature branch, write/generate E2 only after zero-null evidence, update the snapshot, run the compatibility/migration suite, fix premerge findings on that branch, and commit `feat: enforce customer profile ownership`. Merge to development, poll the exact execution, and prove create/replay/read/history plus rollback to the recorded D1 revision. Promote the same R2 commit/artifacts to test and repeat before starting D2.

- [ ] **Step 5: Promote R3 (D2 bridge, no migration) through both environments.** On the current R3 feature branch, remove the `SubjectId` property/mapping/read/write from code and tests while E2 retains the nullable column, run the full suite, fix premerge findings on that branch, and commit `refactor: stop using legacy idempotency subjects`. Merge/deploy to development and then promote the same commit/artifact to test. Run the full smoke matrix in each, retain D2 as latest ready for the documented rollback window, and prove D2 -> D1 rollback before E3.

- [ ] **Step 6: Promote R4 (E3 + D2-compatible behavior) through both environments.** On the current R4 feature branch, add only the E3 drop after the D2 soak/rollback window, run the compatibility/migration suite, fix premerge findings on that branch, and commit `refactor: remove legacy idempotency subject schema`. Merge to development, poll the exact execution, deploy the D2-compatible rebuild, and prove the recorded pre-E3 D2 revision remains healthy. Promote the same R4 commit/artifacts to test and repeat. Record that D1/Sprint 3 are no longer post-E3 rollback targets.

- [ ] **Step 7: Enforce the phase/defect branch distinction.** R2-R4 are planned release phases and each uses its own reviewed `feature/*` branch. Review findings discovered before that phase deploys are fixed on that current phase branch. A defect first discovered after a phase deploys uses a new remediation `feature/*` branch and blocks the next phase. Never let development advance to the next phase until test has received and passed the current one; never reuse development data mappings/evidence for test; never enable production.

### Task 8: Full verification, independent review, Azure smoke, and adversarial QA

**Files:**

- Create: `docs/evidence/sprint-4/{development-verification,development-smoke,test-qa,review}.md`
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md` only after all gates pass

- [ ] **Step 1: Run fresh local repository gates.**

```powershell
dotnet restore CloudOrders.slnx
dotnet format CloudOrders.slnx --verify-no-changes --no-restore
dotnet build CloudOrders.slnx --configuration Release --no-restore
dotnet test CloudOrders.slnx --configuration Release --no-build
az bicep lint --file infra/main.bicep
az bicep build --file infra/main.bicep
az bicep build-params --file infra/environments/development.bicepparam
az bicep build-params --file infra/environments/test.bicepparam
az bicep build-params --file infra/environments/production.bicepparam
git diff --check
```

Expected: every command exits 0; no warnings/failures; production-exclusion tests remain green.

- [ ] **Step 2: Verify EF model and migration SQL at each boundary.** Run `dotnet ef migrations has-pending-model-changes`; generate idempotent E1/E2/E3 scripts; independently review E1 as additive, E2 only after zero-null evidence, and E3 only after D2. Test empty database, Sprint 3 upgrade, reset path, mapped-backfill path, D1 against E1/E2, D2 against E2/E3, failed migration, and rerun idempotency.

- [ ] **Step 3: Inspect SQL directly.** Query `__EFMigrationsHistory`, `sys.key_constraints`, `sys.foreign_keys`, `sys.indexes`, and `sys.columns`. Assert exact migration ID, named profile alternate keys, restrictive ownership FKs, history index order, actor/target uniqueness, `Order.CustomerProfileId == Idempotency.TargetCustomerProfileId`, zero legacy/unowned rows at the applicable gate, and `SubjectId` absent only after E3.

- [ ] **Step 4: Verify security/configuration modes.** Prove missing config fails startup in Development/Test/Production; Testing works only with explicit injected config; health is anonymous; real locally signed JWT negatives produce 401/403 as specified; fake auth is policy-test-only; logs/audit contain no forbidden data. Inspect a real token only through the API behavior/runbook and never retain its value.

- [ ] **Step 5: Verify cursor and rollback across revisions.** Exercise K1-current/K2-validation, K2-current/K1-previous, old cursor on new revision, new cursor on the prior predistributed revision, tamper/target/expiry failures, K1 removal after TTL+rollback overlap, and the recorded D1/D2 schema compatibility matrix.

- [ ] **Step 6: Perform independent review before development merge.** A separate high-capability reviewer checks code, contracts, migrations, Bicep/workflows, runbooks, tests, role/control planes, audit redaction, and evidence. Fix all premerge Critical/Important findings on this current branch, re-run focused/full gates, and re-review.

- [ ] **Step 7: Run live development smoke with realizable identities.** Use one real OTP customer, a second real OTP customer, and the federated existing-work-account admin. Verify `/me`, own create/get/history/replay, cross-customer safe 404, admin access, role revoke plus fresh-token denial, role regrant plus fresh-token access, exact revision/digest/migration/job execution, TLS, health, SQL ownership, and audit. Do not retain auth state or token text.

- [ ] **Step 8: Run nuanced test QA.** Locally fabricated signed JWTs own cryptographic/claim negatives that a live tenant cannot legitimately issue: bad signature, expired/future token, forged issuer/audience/tenant/client, malformed/multiple `oid`, app-only shape, and invented unknown/case-changed roles. Azure `test` QA uses only live-realizable states: no token, two ordinary customers with no app-role assignment, federated admin, read-only vs write scope, role grant/revoke followed by fresh token, absent/foreign parity, concurrency, replay, history boundaries, cursor rotation/tamper, recovery, and direct SQL integrity. Do not pollute the tenant by creating fake roles or weakening registrations merely to manufacture a negative.

- [ ] **Step 9: Close without production.** For any post-deployment defect, create a fresh `feature/*` remediation branch, repeat the affected review/deployment/QA gates, and append re-test evidence. When all gates pass, update Sprint 4 status and record immutable release IDs; do not promote to master or configure production identity.

## Plan self-review checklist

- **Coverage:** Tasks 1-8 cover every token, ownership, migration/rollback, cursor, audit, control-plane, federation, runtime, smoke, and QA requirement while retaining default customers, one initial federated admin, no Graph writes for the API, and no production.
- **Terminology:** The only customer capability term is verified External ID customer; the only elevated product role is `user.admin`; requested scope URIs and bare `scp` comparisons are intentionally distinct.
- **Type consistency:** `AuthenticatedSubject -> CustomerProfile`; `OwnedOrder.Owner.CustomerProfileId` is the read authorization resource; idempotency uses actor and target profile IDs; persistence receives `OrderHistoryBoundary`, while only the API cursor codec handles `OrderHistoryCursorPayload`/strings.
- **File consistency:** ASP.NET policy types remain under `CloudOrders.Api/Identity`; temporary subject-provider files are deleted; `OrderHistoryPage` is the sole history response DTO.
- **Placeholder scan:** implementation values are derived through named tenant/app configuration and protected evidence; the plan contains no unfinished implementation instruction. Before execution, search case-insensitively for placeholder markers and inconsistent type/property names, then correct any result.
