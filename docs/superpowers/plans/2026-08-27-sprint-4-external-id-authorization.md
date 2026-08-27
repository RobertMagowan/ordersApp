# Sprint 4 External ID Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Authenticate individual self-service customers with Microsoft Entra External ID, enforce ownership for every order operation, and permit explicitly assigned `user.admin` users to work across customer records.

**Architecture:** The API validates a user-delegated External ID access token before creating or resolving a race-safe `CustomerProfile` keyed by immutable issuer and object ID. Endpoint-level policies require the correct OAuth scope, while a resource authorization handler permits a profile's own resources or an explicit `user.admin` role. Orders and idempotency records bind to the profile ID rather than a client-selected reference.

**Tech Stack:** .NET 10/C# 14, ASP.NET Core Minimal APIs/JWT bearer authorization, EF Core 10/Azure SQL, Testcontainers SQL Server, Microsoft Entra External ID, Bicep, GitHub Actions OIDC.

## Global Constraints

- Work only on `feature/*`; promotion remains feature → development → test → master through pull requests. Production is out of scope.
- Do not run EF migrations from API startup, grant Microsoft Graph write permissions to the application, commit secrets, weaken TLS, or store browser authentication state.
- Customers self-register through External ID passwordless email one-time-code flow. A verified External ID identity is an effective default customer capability; `user.admin` is an explicit, case-sensitive Entra application-role value.
- The API accepts only user-delegated tokens with exact configured issuer, tenant, audience, signature/lifetime, `oid`, and `Orders.Read` or `Orders.Write` scope. It rejects app-only tokens and unknown roles.
- Product `user.admin` and the least-privileged Entra directory role that grants product app roles are separate control planes. Maintain two emergency directory administrators outside the product identity path.
- Retain `customerReference` in the v1 POST contract only as a server-validated compatibility field. The API resolves ownership by `CustomerProfileId`; foreign and absent customer resources return `404`.
- Write a failing focused test before each production change, use four-space C# indentation, keep warnings at zero, and record sanitized development/test evidence under `docs/evidence/sprint-4/`.

---

## File structure

| Path | Responsibility |
|---|---|
| `src/CloudOrders.Infrastructure/Persistence/CustomerProfileEntity.cs` | Durable immutable subject-to-customer ownership record. |
| `src/CloudOrders.Application/Identity/*` | Principal/profile abstractions, policy requirements, and ownership decisions. |
| `src/CloudOrders.Api/Identity/*` | JWT options validation, claims normalization, and test-only authentication support. |
| `src/CloudOrders.Infrastructure/Persistence/*Repository.cs` | Transactional profile resolution and profile-scoped order reads/history. |
| `src/CloudOrders.Contracts/Identity/*`, `src/CloudOrders.Contracts/Orders/*` | `/me` and cursor-history wire contracts. |
| `infra/modules/container-app.bicep`, `infra/main.bicep` | Non-secret runtime identity configuration and container-app secret references. |
| `ops/runbooks/external-id-setup.md` | Manual External ID tenant, user-flow, role, recovery, and drift procedure. |

## Task 1: Freeze the External ID contract and test-auth boundary

**Files:**
- Modify: `docs/contracts/v1-contracts.md`, `docs/contracts/traceability.md`, `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`
- Create: `src/CloudOrders.Api/Identity/ExternalIdentityOptions.cs`, `tests/CloudOrders.IntegrationTests/TestAuthenticationHandler.cs`, `tests/CloudOrders.ArchitectureTests/ExternalIdentityContractTests.cs`

**Interfaces:**

```csharp
public sealed class ExternalIdentityOptions
{
    public required string Authority { get; init; }
    public required string TenantId { get; init; }
    public required string Audience { get; init; }
    public required string AllowedClientId { get; init; }
    public required string ReadScope { get; init; }
    public required string WriteScope { get; init; }
    public const string AdminRole = "user.admin";
}
```

- [ ] **Step 1: Write failing contract and architecture tests**

```csharp
[Fact]
public void ContractDefinesExternalIdDefaultCustomerAndUserAdminRole()
{
    Assert.Contains("user.admin", contract);
    Assert.Contains("CustomerProfiles", contract);
    Assert.Contains("issuer", contract, StringComparison.OrdinalIgnoreCase);
}
```

Run: `dotnet test tests/CloudOrders.ArchitectureTests --filter FullyQualifiedName~ExternalIdentityContractTests --configuration Release`

Expected: FAIL because the contract/options/test authentication boundary does not exist.

- [ ] **Step 2: Define the exact security contract**

Update the contract pack and traceability from group-to-customer mapping to issuer-plus-object-ID profiles, effective customers, `user.admin`, delegated scopes, `404` parity, profile-based idempotency, verified-contact email, and safe audit fields. Keep existing event customer references and prohibit email/token payloads in telemetry.

- [ ] **Step 3: Add validated options and deterministic test authentication**

Add `ExternalIdentityOptions` and startup validation outside explicit `Testing`/local test mode. Implement a test-only authentication handler that emits controlled `iss`, `tid`, `aud`, `oid`, `scp`, `azp`, `roles`, and token-type claims; production code must never register it.

- [ ] **Step 4: Verify the focused test turns green**

Run: `dotnet test tests/CloudOrders.ArchitectureTests --filter FullyQualifiedName~ExternalIdentityContractTests --configuration Release`

Expected: PASS.

- [ ] **Step 5: Commit the contract boundary**

```powershell
git add docs/contracts docs/superpowers/plans src/CloudOrders.Api/Identity tests/CloudOrders.ArchitectureTests tests/CloudOrders.IntegrationTests/TestAuthenticationHandler.cs
git commit -m "docs: define External ID authorization contract"
```

## Task 2: Add profile-owned SQL persistence and migration

**Files:**
- Modify: `src/CloudOrders.Infrastructure/Persistence/{CloudOrdersDbContext.cs,OrderEntity.cs,IdempotencyRecordEntity.cs,OrderPersistenceMapper.cs,SqlOrderRepository.cs,SqlIdempotentOrderStore.cs}`, `src/CloudOrders.Application/Abstractions/{IOrderRepository.cs,IIdempotentOrderStore.cs,ISubjectIdProvider.cs}`
- Create: `src/CloudOrders.Infrastructure/Persistence/{CustomerProfileEntity.cs,Configurations/CustomerProfileEntityConfiguration.cs,SqlCustomerProfileStore.cs}`, `src/CloudOrders.Application/Identity/{AuthenticatedSubject.cs,ICustomerProfileStore.cs,CustomerProfile.cs}`, EF migration, `tests/CloudOrders.IntegrationTests/CustomerProfileSqlIntegrationTests.cs`

**Interfaces:**

```csharp
public sealed record AuthenticatedSubject(string Issuer, Guid ObjectId, string? VerifiedEmail);
public sealed record CustomerProfile(Guid Id, string CustomerReference, string Issuer, Guid ObjectId);
public interface ICustomerProfileStore
{
    Task<CustomerProfile> GetOrCreateAsync(AuthenticatedSubject subject, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing real-SQL tests**

Cover profile creation, simultaneous first access for one `(issuer, oid)`, same email with a different `oid`, generated reference format, and a profile-scoped order query. Assert exactly one profile after the race and no email-based relink.

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter FullyQualifiedName~CustomerProfileSqlIntegrationTests --configuration Release`

Expected: FAIL because the profile tables and store do not exist.

- [ ] **Step 2: Add the ownership schema**

Create `CustomerProfiles` with `Id`, `Issuer`, `ObjectId`, opaque generated `CustomerReference`, nullable `ContactEmail`, timestamps, and row version. Enforce unique `(Issuer,ObjectId)` and unique `CustomerReference`. Add required `Orders.CustomerProfileId`, FK restriction, and `(CustomerProfileId, CreatedAt DESC, Id DESC)` index. Change idempotency's application subject from the local string to profile ID.

- [ ] **Step 3: Implement atomic/race-safe profile resolution**

Insert a profile only after authenticated policy success. On the profile unique-key race, create a new context and re-read by exact issuer/object ID. Never query by email. Generate a reference that satisfies the current 1–64 reference grammar and expose no identifier derived from raw email.

- [ ] **Step 4: Define and test existing-row treatment**

Write migration tests for empty database and upgrade database. Before applying non-production migration, either backfill each existing order only when an approved profile mapping exists or execute the documented non-production reset; do not invent mappings from references/emails.

- [ ] **Step 5: Verify and commit the persistence slice**

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~CustomerProfileSqlIntegrationTests|FullyQualifiedName~OrderSqlIntegrationTests" --configuration Release`

Expected: PASS.

```powershell
git add src/CloudOrders.Application src/CloudOrders.Infrastructure tests/CloudOrders.IntegrationTests
git commit -m "feat: bind orders to authenticated customer profiles"
```

## Task 3: Authenticate tokens and authorize customer resources

**Files:**
- Modify: `src/CloudOrders.Api/Program.cs`, `src/CloudOrders.Application/Orders/{CreateOrderHandler.cs,GetOrderHandler.cs,IdempotencyRequestHasher.cs}`
- Create: `src/CloudOrders.Api/Identity/{ScopeRequirement.cs,ScopeAuthorizationHandler.cs,CustomerResourceRequirement.cs,CustomerResourceAuthorizationHandler.cs,CurrentCustomerProfileAccessor.cs}`, `tests/CloudOrders.IntegrationTests/AuthorizationIntegrationTests.cs`, `tests/CloudOrders.UnitTests/CustomerResourceAuthorizationHandlerTests.cs`

**Interfaces:**

```csharp
public sealed record CustomerResource(Guid CustomerProfileId);
public interface ICurrentCustomerProfileAccessor
{
    Task<CustomerProfile> GetRequiredAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing token-negative and ownership tests**

Test anonymous, invalid issuer, wrong tenant/audience, missing/incorrect scope, missing/malformed `oid`, application token, unknown role, own resource, foreign resource, and exact `user.admin` role. Assert `401` for invalid authentication and `404` for foreign resources.

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter FullyQualifiedName~AuthorizationIntegrationTests --configuration Release`

Expected: FAIL because order endpoints are anonymous.

- [ ] **Step 2: Add bearer authentication and named policies**

Configure `AddAuthentication().AddJwtBearer()` from validated options; call `UseAuthentication()` before `UseAuthorization()`. Add `OrdersRead` and `OrdersWrite` policies that require delegated scope and a human `oid`; reject app-only tokens and unknown roles. Register health routes as `AllowAnonymous` and order routes as `RequireAuthorization`.

- [ ] **Step 3: Add resource authorization**

Implement `CustomerResourceAuthorizationHandler`: succeed only for exact profile ID or the exact `user.admin` role. Centralize the decision through `IAuthorizationService`; never duplicate ownership `if` statements in handlers. Resolve the profile once per request and use profile ID in the canonical idempotency hash and store key.

- [ ] **Step 4: Re-authorize replay and read paths**

Perform policy/resource checks before create, exact replay, get-by-ID, and history responses. A token with a removed admin role cannot reuse an already-issued replay response after it receives a new token.

- [ ] **Step 5: Verify and commit**

Run: `dotnet test tests/CloudOrders.UnitTests --filter FullyQualifiedName~CustomerResourceAuthorizationHandlerTests --configuration Release`

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter FullyQualifiedName~AuthorizationIntegrationTests --configuration Release`

Expected: PASS.

```powershell
git add src/CloudOrders.Api src/CloudOrders.Application tests/CloudOrders.UnitTests tests/CloudOrders.IntegrationTests
git commit -m "feat: authorize customer-owned order resources"
```

## Task 4: Implement profile discovery and authorized customer history

**Files:**
- Modify: `src/CloudOrders.Api/Program.cs`, `src/CloudOrders.Application/Abstractions/IOrderRepository.cs`, `src/CloudOrders.Infrastructure/Persistence/SqlOrderRepository.cs`, `src/CloudOrders.Contracts/Orders/OrderResponse.cs`
- Create: `src/CloudOrders.Contracts/Identity/CurrentCustomerResponse.cs`, `src/CloudOrders.Contracts/Orders/{OrderHistoryResponse.cs,OrderHistoryPage.cs}`, `src/CloudOrders.Application/Orders/{GetCurrentCustomerHandler.cs,ListCustomerOrdersHandler.cs,OrderHistoryCursor.cs}`, `tests/CloudOrders.IntegrationTests/OrderHistoryIntegrationTests.cs`

**Interfaces:**

```csharp
public sealed record CurrentCustomerResponse(string CustomerReference);
public sealed record OrderHistoryPage(IReadOnlyList<OrderResponse> Items, string? NextCursor);
Task<OrderHistoryPage> ListAsync(Guid customerProfileId, string? cursor, int pageSize, CancellationToken cancellationToken);
```

- [ ] **Step 1: Write failing API tests**

Test authenticated `GET /api/v1/me`; customer POST using its discovered reference; foreign POST/history/order-ID `404`; admin access; newest-first `(CreatedAt,Id)` ordering; page sizes 1–100; opaque tamper/expiry/unsupported cursor `400`.

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter FullyQualifiedName~OrderHistoryIntegrationTests --configuration Release`

Expected: FAIL because `/api/v1/me` and history do not exist.

- [ ] **Step 2: Implement profile discovery and v1 POST comparison**

Map `GET /api/v1/me` with `OrdersRead` policy and return only server-owned `CustomerReference`. Preserve current POST wire shape, then resolve the caller profile and return `404` when the submitted reference is not that profile's reference unless `user.admin` targets an existing profile.

- [ ] **Step 3: Implement cursor history**

Add `GET /api/v1/customers/{customerReference}/orders`. Resolve target reference to a profile, authorize that profile as a resource, query only that profile, and use a versioned HMAC-authenticated base64url cursor containing `(CreatedAtUtc,Id)`. Reject invalid/expired/version-mismatched cursor states with `invalid_cursor`; never include email or claims in the cursor.

- [ ] **Step 4: Add stable signing-key configuration**

Use a versioned cursor signing-key provider with current and previous keys so rolling revisions validate both during rotation. Require production-like configuration outside test mode; do not use an in-memory key outside tests.

- [ ] **Step 5: Verify and commit**

Run: `dotnet test tests/CloudOrders.IntegrationTests --filter "FullyQualifiedName~OrderHistoryIntegrationTests|FullyQualifiedName~AuthorizationIntegrationTests" --configuration Release`

Expected: PASS.

```powershell
git add src/CloudOrders.Api src/CloudOrders.Application src/CloudOrders.Contracts src/CloudOrders.Infrastructure tests/CloudOrders.IntegrationTests
git commit -m "feat: add authorized customer order history"
```

## Task 5: Configure non-production runtime and Entra operations

**Files:**
- Modify: `infra/{main.bicep,modules/container-app.bicep,environments/development.bicepparam,environments/test.bicepparam}`, `.github/workflows/deploy.yml`, `README.md`, `AGENTS.md`
- Create: `ops/runbooks/{external-id-setup.md,external-id-role-operations.md,cursor-key-rotation.md}`, `tests/CloudOrders.ArchitectureTests/ExternalIdentityInfrastructureTests.cs`

- [ ] **Step 1: Write failing infrastructure/policy tests**

Assert non-production parameter overlays expose no identity secrets; the API receives required non-secret authority/tenant/audience/client/scope/role settings; cursor signing material uses a Container App secret reference rather than plain environment value; production is not enabled by the sprint configuration.

Run: `dotnet test tests/CloudOrders.ArchitectureTests --filter FullyQualifiedName~ExternalIdentityInfrastructureTests --configuration Release`

Expected: FAIL because identity settings/runbooks are absent.

- [ ] **Step 2: Add fail-closed Bicep/runtime configuration**

Pass only immutable External ID identifiers and expected scope/role values through Bicep. Add the cursor-key secret reference with a documented owner, rotation procedure, and previous-key overlap. Do not embed tenant secrets, user-flow passwords, Graph credentials, or customer email in parameter files or workflow logs.

- [ ] **Step 3: Write executable Entra runbooks**

Document: create External ID tenant; configure email OTP user flow; register API/client; define `user.admin`; federate the work tenant for the bootstrap product admin; assign/revoke product roles; maintain emergency directory admins; verify audit logs; record role/app IDs and secret owner/expiry only in approved secure stores. State that directory privileges and product roles are separate.

- [ ] **Step 4: Validate and commit**

Run: `az bicep lint --file infra/main.bicep`

Run: `az bicep build-params --file infra/environments/development.bicepparam; az bicep build-params --file infra/environments/test.bicepparam`

Run: `dotnet test tests/CloudOrders.ArchitectureTests --configuration Release`

Expected: all commands succeed with no errors.

```powershell
git add infra .github/workflows README.md AGENTS.md ops/runbooks tests/CloudOrders.ArchitectureTests
git commit -m "feat: configure External ID authorization runtime"
```

## Task 6: Verify, review, and promote the non-production release

**Files:**
- Create: `docs/evidence/sprint-4/{development-verification.md,development-smoke.md,test-qa.md,review.md}`
- Modify: `docs/superpowers/plans/2026-08-16-cloudorders-sprint-implementation-plan.md`

- [ ] **Step 1: Run full local verification and state inspection**

Run: `dotnet restore CloudOrders.slnx; dotnet format CloudOrders.slnx --verify-no-changes --no-restore; dotnet build CloudOrders.slnx --configuration Release --no-restore; dotnet test CloudOrders.slnx --configuration Release --no-build`

Inspect Testcontainers SQL migration history, profile uniqueness, orders/profile FK, outbox linkage, and idempotency behavior using only synthetic data. Record exact commands and sanitized outcomes.

- [ ] **Step 2: Perform independent development verification and review**

Use a separate high-capability verifier to execute the identity-negative matrix, profile/upsert race, customer/admin matrix, cursor tamper/rotation, migration upgrade, audit-redaction, Bicep/workflow validation, and review. Fix every Critical/Important finding on a fresh `feature/*` branch and re-review.

- [ ] **Step 3: Configure and smoke development**

After the reviewed PR merges to `development`, provision the approved External ID development configuration through the runbook, deploy, and test interactive OTP customer sign-in plus federated admin sign-in. Verify `/me`, own create/read/history, foreign `404`, admin access, expected revision/digest, TLS, health, and SQL state. Never save browser sign-in state.

- [ ] **Step 4: QA test environment**

After the reviewed `development` → `test` PR deploys, a QA-only agent tests two External ID customer accounts, one federated admin, malformed/expired/wrong issuer/audience/scope/role scenarios, cross-customer `404`, replay after new token, cursor tamper, role grant/revoke token refresh, recovery, concurrent first access, and direct SQL ownership integrity. Fix any defect on a new `feature/*` branch and repeat affected gates.

- [ ] **Step 5: Close Sprint 4 without production deployment**

Record immutable release identifiers, migration ID, tenant/app/user-flow identifiers without secrets, manual-role evidence, commands, outcomes, and defects. Update roadmap status only after QA passes. Do not create a test → master PR or deploy production.

## Plan self-review

- **Spec coverage:** Tasks 1–5 implement every identity-plane, token, ownership, audit, runtime, and compatibility requirement; Task 6 applies the required development verification, independent review, Azure smoke, and test QA gates.
- **No placeholders:** the plan has explicit file paths, test behavior, interfaces, commands, commits, and manual External ID steps. Production deployment and Graph write privileges are explicitly excluded.
- **Type consistency:** `AuthenticatedSubject` resolves to `CustomerProfile`, `CustomerProfile.Id` is the ownership key for orders and idempotency, and `CustomerResource` is the authorization resource used by endpoints.
