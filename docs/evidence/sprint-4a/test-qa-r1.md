# Sprint 4A R1 test QA evidence

Date: 2026-09-06  
Environment: `test`  
Immutable deployment: commit `778ae7e08130d3429ff7120055adf1dbcd3efd6d`; workflow run `34061359073`; revision `cloudorders-test-api--0000003`.

## Transition and deployment

The promotion workflow completed its test foundation, release preview, Azure SQL preview and provision, SQL migration, and application deployment jobs successfully. The deployed image was `cloudorderst583431testacr.azurecr.io/cloudorders-api@sha256:89c345369e75777b9879d67a170c8929360e41a9ba2c7b4565a95f7c893690fb`. The dedicated E1-only migration job was deliberately skipped because the released migration path had already been reconciled.

## Independent QA checks

| Check | Expected result | Outcome |
| --- | --- | --- |
| Live and ready probes | HTTP 200; ready reports healthy | Pass |
| No bearer token / invalid bearer token | HTTP 401 without resource disclosure | Pass |
| Verified customer identity | Authenticated request succeeds only for its current customer context | Pass |
| Deployment state | One active revision using the immutable image | Pass |
| SQL transition | Provision and migration jobs complete before application deployment | Pass |

The authenticated check used a real verified identity, but no email address, token, customer reference, or other personal data is recorded here. No defects were found in this focused test run and the probes were repeated after the final application deployment.

## Supporting regression coverage

The local Sprint 4A verification covers two seeded customer profiles, ownership isolation, foreign-resource concealment, `user.admin` elevation, scoped JWTs, idempotency, SQL persistence, and audit-data minimisation. See [task-4-local-verification.md](task-4-local-verification.md). This is supporting automated coverage; the table above is the live test-environment acceptance record.

## QA disposition

QA passed for the listed immutable deployment. Production was not deployed.
