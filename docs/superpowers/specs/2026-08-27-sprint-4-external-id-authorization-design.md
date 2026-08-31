# Sprint 4 External ID Authorization Design

## Goal

Protect every order by the authenticated individual customer, while allowing explicitly assigned administrators to work across customers. Customer self-service sign-up must use Microsoft Entra External ID and must never permit privilege self-escalation.

## Identity planes

Customer identities live in a dedicated Microsoft Entra External ID tenant. They use the standard passwordless email one-time-code sign-up/sign-in flow. A verified customer is an effective default customer capability; this is not a self-assigned Entra app-role claim.

The initial product administrator is the current work account, federated for product sign-in and explicitly assigned the `user.admin` app role. Product administration and directory administration are distinct: a least-privileged directory administrator assigns app roles in the Entra portal, while `user.admin` authorizes product-wide access only. Maintain two emergency directory-admin accounts outside the product identity path.

Future elevated roles are explicit, case-sensitive Entra application-role values with a `user.` prefix. No customer can assign roles. `user.admin` is the only elevated role in Sprint 4; future roles receive dedicated policies and tests.

## Token and policy model

The API accepts only user-delegated tokens with configured exact issuer, tenant, audience, signature, lifetime, `oid`, and delegated scope. `Orders.Read` and `Orders.Write` remain the OAuth client-to-API boundary; `user.admin` is an additional user privilege. Reject app-only tokens, missing/unknown claims, wrong audiences, scopes, issuers, or role values.

Minimal API order routes use `RequireAuthorization` for the coarse policy. A resource-based `IAuthorizationService` handler resolves the caller profile and checks ownership. It permits a target customer only when the profile matches or the token has `user.admin`; the admin role never bypasses token or scope validation. Health endpoints remain deliberately anonymous.

## Ownership and persistence

Add `CustomerProfiles` keyed by immutable `(Issuer, ObjectId)` and containing `ProfileId`, a generated opaque `CustomerReference`, nullable verified-contact email, lifecycle timestamps, and a row version. The profile upsert is race-safe and never links accounts by email. Recreated identities receive new profiles.

Add non-null `Orders.CustomerProfileId`, an FK to `CustomerProfiles`, and an ownership-history index. Query and authorize by `ProfileId`; retain `CustomerReference` only for v1 wire compatibility and display. Bind idempotency to `CustomerProfileId`. Existing non-production rows require an explicitly reviewed expand/backfill/contract migration or non-production reset before release.

Expose authenticated `/api/v1/me` to return the caller's server-owned customer reference. Until a v2 contract removes the field, POST keeps `customerReference` but the API requires an exact match with the resolved profile. Foreign or nonexistent customer resources return `404`.

## Operations, audit, and testing

External ID tenant, user flow, app registrations, app-role GUIDs, delegated scopes, exact redirect URIs, and assignments are tenant-managed configuration, not Bicep resources. Store only non-secret identifiers in protected environment configuration; validate them on startup outside explicit test mode. Keep setup, role-grant/revoke, recovery, token-lifetime, and drift-evidence runbooks.

Audit events allowlist action, result, actor profile ID, target IDs, validated capability, trace ID, and environment. They never include token material, claims dumps, raw email, request bodies, or order payloads. Entra app-role assignment audit is reviewed separately from API audit.

Automated coverage includes token-validation negatives, scope/role matrices, profile-upsert races, cross-customer `404` parity, same-email/new-identity isolation, idempotency across profiles, admin grant/revoke with new tokens, audit redaction, and empty/upgrade migration paths. Development and test verification use interactive OTP customer sign-in and federated admin sign-in without storing browser authentication state.

## Scope

Sprint 4 updates the version-1 contract pack, traceability, API, SQL migration, Bicep runtime configuration, CI checks, and non-production evidence. It does not deploy to production, create an in-app Entra role-management console, or grant Microsoft Graph write permissions to the application.
