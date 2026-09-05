# Sprint 4A Task 3 report

## Scope delivered

- Added the application `AuthenticatedSubject`, `CustomerProfile`, profile-store boundary, and opaque reference-generator boundary.
- Added SQL customer-profile resolution with exact issuer/object-id identity, immutable insert-only contact email, five-attempt named-reference collision recovery, and fresh-context issuer/object-id race recovery.
- Added the additive `AddCustomerProfileOwnershipExpand` migration, snapshot, nullable ownership columns, named restrictive FKs/indexes, and the E1 migration-only manifest.
- Removed the temporary Sprint 3 subject-provider files and registration. Existing pre-D1 request behavior retains its deterministic legacy subject value until the owner-aware D1 slice replaces it.

## TDD evidence

- RED: `dotnet test tests\CloudOrders.UnitTests --filter FullyQualifiedName~CustomerReferenceGeneratorTests --configuration Release` failed because `CustomerReferenceGenerator` did not exist.
- GREEN: the same focused test passed (1/1).
- RED: `dotnet test tests\CloudOrders.IntegrationTests --filter FullyQualifiedName~CustomerProfileSqlIntegrationTests --configuration Release` failed because `SqlCustomerProfileStore` did not exist.
- RED: `dotnet test tests\CloudOrders.ArchitectureTests --filter FullyQualifiedName~DeploymentWorkflowPolicyTests --configuration Release` failed after requiring the E1 manifest because the manifest did not exist.
- GREEN: focused unit tests for the generator and updated handler passed (3/3); the focused deployment-workflow architecture tests passed (8/8).

## Migration verification

- `dotnet ef migrations has-pending-model-changes --project src\CloudOrders.Infrastructure --startup-project src\CloudOrders.Migrations --configuration Release` exited 0 with no pending model changes.
- Generated the idempotent script from `20260816221235_InitialSqlPersistence` to `AddCustomerProfileOwnershipExpand`; review found only additive table/columns/indexes/FKs in the Up path, with no drop, rename, non-null alteration, or data mutation.

## Environmental limitation

An already-running `testhost` process (PID 46344) held the integration-test output DLLs throughout focused SQL-test and solution-build attempts. The focused Testcontainers profile/migration/order tests and full solution Release build could not be executed without interrupting another worker's process; no process was terminated.

## Commit

- `6d7ad75 feat: expand customer profile ownership schema`

## Review minor follow-up: unverified-email end-to-end coverage

- Added `EmailVerifiedFalseFlowsThroughSubjectReaderAndStoresNoContactEmail`, which creates a raw claims principal with one email and `email_verified=false`, reads it through `AuthenticatedSubjectReader`, and passes the resulting subject to `SqlCustomerProfileStore`.
- RED: with the reader temporarily treating any present `email_verified` claim as verified, `dotnet test tests\CloudOrders.IntegrationTests --filter FullyQualifiedName~EmailVerifiedFalseFlowsThroughSubjectReaderAndStoresNoContactEmail --configuration Release` failed as expected: the stored contact email was `customer@example.test`, rather than `null`.
- GREEN: restored the exact literal `true` check; the same focused Testcontainers test passed (1/1).
- Focused verification: `AuthenticatedSubjectReaderTests` passed (4/4) and `CustomerProfileSqlIntegrationTests` passed (5/5).
