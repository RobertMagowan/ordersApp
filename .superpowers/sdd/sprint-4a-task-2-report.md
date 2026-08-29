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
