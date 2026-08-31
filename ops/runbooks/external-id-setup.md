# External ID setup

## Scope and record keeping

This runbook is a manual control-plane procedure. It must be performed in the approved External ID and workforce tenants; no Bicep, GitHub workflow, or application startup action creates registrations, users, assignments, or secrets. Record actual tenant identifiers, subdomain, object identifiers, owner names, secret expiry, screenshots, and token evidence only in the protected Sprint 4A evidence store. Do not copy those values into this repository.

## Ownership record

Before enabling a non-production deployment, the named identity owner records these accountable roles in the protected evidence store: External ID tenant owner and subdomain custodian; two separate emergency Global Administrators; routine Cloud Application Administrator; API application owner; PKCE public-client owner; workforce-federation application and secret owner with expiry; user-flow owner; GitHub environment owner; and data-transition approver. Directory administration and the product `user.admin` assignment are separate capabilities and must not be held or evidenced as the same authorization decision.

## External ID applications and flow

Create or verify a single-tenant API registration with an App ID URI formed as `api://{api-client-id}`, v2 access tokens, delegated `Orders.Read` and `Orders.Write` scopes, and a single `user.admin` role restricted to `User` members. The API registration has no Microsoft Graph permission.

Create or verify the separate native/public PKCE client with the exact `http://localhost` redirect URI and no secret. Preauthorize it for both API scopes. Add both applications to the email one-time-code sign-up/sign-in user flow and set the API enterprise application’s Assignment required setting to No.

## Workforce federation

In the workforce tenant, create or verify one single-tenant confidential federation application. Configure exactly the two External ID federation redirect URI forms ending in `/{external-tenant-id}/federation/oauth2` and `/{tenant-subdomain}.onmicrosoft.com/federation/oauth2`. Grant only delegated `openid`, `profile`, `email`, and `User.Read`; do not grant Graph write or application permissions. Store its named, expiring secret only in the External ID identity-provider configuration.

Add the Microsoft Entra ID provider with its workforce issuer, map only verified contact claims, and attach it to the user flow. Sign in the approved work account and have the routine directory administrator assign `user.admin` to the resulting External ID user object. Record a fresh-token check that the external tenant `tid` and `oid`, bare API scopes, and exact role are present. Never use the workforce object ID as an application profile key.

## Runtime handoff

Place the five non-secret values in the protected `development` and `test` GitHub environment variables named by the deployment workflow. Enable the setting only after the protected evidence has been reviewed. Production remains disabled.
