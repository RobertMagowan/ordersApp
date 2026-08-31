# Sprint 4A Task 2 evidence

## Implemented

- Added centrally pinned `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11 and real bearer validation with HTTPS metadata, raw claims, exact issuer/audience/lifetime/signature settings, and validation of `tid`, `azp`, user `oid`, and delegated `scp`.
- Added fail-fast External ID options validation, authenticated-subject parsing, exact read/write policies, known-role enforcement, safe 401/403 Problem Details, and correct authentication/authorization middleware order.
- Added RSA-signed local JWT factory/static OIDC metadata tests. The JWT authentication scheme is the production bearer scheme in these tests.
- Added policy-only test authentication in integration-test services and an architecture guard preventing its production reference. Existing SQL order test fixtures now use that explicit policy-only fixture to preserve their data assertions.

## TDD evidence

1. Red: `dotnet test tests\\CloudOrders.UnitTests --filter FullyQualifiedName~AuthenticatedSubjectReaderTests --configuration Release` failed with `CS0234` because `CloudOrders.Api.Identity` and the API project reference did not exist.
2. Green: the same focused suite passed 4/4 after the identity reader/API reference were added.

## Verification

- `dotnet test tests\\CloudOrders.UnitTests --filter FullyQualifiedName~AuthenticatedSubjectReaderTests --configuration Release --no-restore`: PASS, 4/4.
- `dotnet test tests\\CloudOrders.IntegrationTests --filter "FullyQualifiedName~JwtBearerAuthenticationTests|FullyQualifiedName~AuthorizationPolicyIntegrationTests|FullyQualifiedName~ExternalIdentityStartupTests" --configuration Release --no-restore`: PASS, 20/20.
- `dotnet test tests\\CloudOrders.IntegrationTests --filter FullyQualifiedName~OrderSqlIntegrationTests --configuration Release --no-restore`: PASS, 17/17 after the explicit policy test fixture update.
- `dotnet format --verify-no-changes --no-restore`: passed before the final SQL test-fixture formatting change; final formatting/full-suite rerun remains required.
- `dotnet build CloudOrders.slnx --configuration Release --no-restore`: PASS, 0 warnings and 0 errors before the final SQL test-fixture update; final build/full-suite rerun remains required.

## Commit

`66b49f4 feat: validate External ID bearer tokens`

## Remaining concern

The commit was made after the focused order SQL suite passed. The final repository-wide format, Release build, and full suite need rerunning after the last fixture change; Docker-backed tests are available and should be included in that final run.

## Review remediation

- Replaced `OrderSqlIntegrationTests` direct `PolicyTestAuthenticationHandler` registration and `PolicyTest:AllowAll` bypass with `OrderSqlJwtBearerWebApplicationFactory`. Each SQL request now carries a locally RSA-signed token validated by the production JWT bearer handler and constrained to the required `Orders.Read Orders.Write` delegated scopes.
- Changed API binding tests to use the same real signed JWT path. `PolicyTestAuthenticationHandler` remains limited to the dedicated authorization-policy matrix.
- Added real bearer regressions for unsigned tokens, expired and not-yet-valid lifetimes, missing and multiple `oid` claims, and an app-only token shape. Invalid-token challenges preserve the bearer handler's safe `error="invalid_token"` header while returning the existing safe problem response.
- Added startup validation coverage for malformed tenant/audience/client GUIDs; HTTP, leading-whitespace, and trailing-slash URIs; issuer tenant mismatch; and case-insensitive duplicate client GUIDs. Client duplicate validation now compares parsed GUID values.
- Added zero JWT clock skew so future/expired lifetime rejection is exact, and extended the architecture guard to reject both `PolicyTestAuthenticationHandler` and the `PolicyTest` scheme from production source projects.

### Review TDD and verification evidence

1. Red: the new JWT test set initially failed to compile because the signed-token factory had no unsigned, duplicate-`oid`, or app-only helpers. After helpers were added, unsigned/lifetime/subject/app-only tests exposed the result handler's bare bearer challenge and the default lifetime clock skew.
2. Green: `dotnet test tests\\CloudOrders.IntegrationTests --filter "FullyQualifiedName~JwtBearerAuthenticationTests|FullyQualifiedName~ExternalIdentityStartupTests" --configuration Release --no-restore` passed 29/29.
3. Red: after replacing the SQL policy handler, `OrderSqlIntegrationTests` returned 403 because its constrained test token initially requested only `Orders.Read` for write routes.
4. Green: after explicitly requesting `Orders.Read Orders.Write`, `dotnet test tests\\CloudOrders.IntegrationTests --filter FullyQualifiedName~OrderSqlIntegrationTests --configuration Release --no-restore` passed 17/17.
5. `dotnet test tests\\CloudOrders.IntegrationTests --filter FullyQualifiedName~ApiTests --configuration Release --no-restore`: passed 10/10.
6. `dotnet test tests\\CloudOrders.ArchitectureTests --filter FullyQualifiedName~ProductionProjectsDoNotReferencePolicyTestAuthentication --configuration Release --no-restore`: passed 1/1.
7. `dotnet format --verify-no-changes --no-restore`: passed.
8. `dotnet build CloudOrders.slnx --configuration Release --no-restore`: passed with 0 warnings and 0 errors.

The repository-wide integration/full-suite invocation was started after these changes but the execution host returned before the Docker-backed integration run produced its final summary. Focused Docker-backed SQL coverage is green; rerun the complete suite in an environment that allows the command to complete uninterrupted.
