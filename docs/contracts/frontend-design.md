# CloudOrders frontend design contract (v1)

**Source handoff section: 19**
**Contract version:** 1.0.0
**Repository normalization:** the source's `staging` environment is named `test` in this repository. The promotion path is `feature/*` → `development` → `test` → `master`.

This document versions the frontend-design authority for CloudOrders. It is binding for the frontend work beginning in Sprint 10; it does not authorize implementation before its dependent API, authorization, and deployment contracts are delivered.

## Product and delivery boundary

CloudOrders.Web is a standalone .NET 10 Blazor WebAssembly application. It calls CloudOrders.Api over HTTPS, exercising the real browser, API, authentication, and CORS boundary. Its companion boundaries are an isolated generated/focused `CloudOrders.Api.Client` and the non-production-only `CloudOrders.TestSupport.Api`.

The primary user is an order-support operator who creates an order from a customer reference and product SKU, finds it again, and understands its current business status. The non-production Observability Lab is for authorized engineers demonstrating or diagnosing reliability scenarios.

The primary routes are `/`, `/orders/new`, `/orders/{orderId}`, `/customers/{customerId}/orders`, `/access-denied`, and `/observability-lab` (development/test only).

## Visual and business-status contract

The UI is a calm *dispatch control* surface inspired by parcel-routing labels and handoff checkpoints, rather than a generic administrative dashboard.

| Token | Value | Use |
|---|---:|---|
| Dispatch Ink | `#17252F` | primary text and navigation |
| Route Blue | `#1769AA` | primary action and current route |
| Signal Teal | `#087E8B` | successful handoff/processing |
| Hold Amber | `#C56A00` | delayed/retrying |
| Fault Red | `#B53737` | failed/dead-lettered diagnostics |
| Mist | `#F3F7F8` | page surface |

Use Manrope for restrained headings, Source Sans 3 for body/forms/navigation, and IBM Plex Mono for identifiers, timestamps, and diagnostic values. Fonts are local or delivered by an approved privacy-conscious method; the application has no undocumented third-party runtime dependency.

The signature component is an accessible business route: `Received → Processing`. In version 1, Processing is the terminal business status: the asynchronous handler accepted the order for downstream fulfillment. Stored, Published, Retried, and Dead-lettered are infrastructure states. They appear only in the authorized non-production diagnostic panel or Observability Lab and are never ordinary-user fulfillment promises. State advancement may animate once, respects `prefers-reduced-motion`, and always supplies text, icons, and ARIA descriptions in addition to color.

## Pages and components

- Home provides order lookup, authorized recent orders, and a Create order action.
- Create order provides customer/product selection, quantity, review, durable-idempotent submission, success routing, and recoverable errors.
- Order details provides the business route, created/updated times, customer/product summary, refresh behavior, and an authorized non-production diagnostics drawer.
- Customer history provides API-supported filters/sorts only, stable cursor pagination, empty/retry states, and URL-preserved query state.
- Authentication views provide sign-in, access-denied, session-expired, and return-to-route behavior.
- Observability Lab provides scenario catalogue, safety notice, configuration, active lease, timeline, correlation IDs, cleanup status, KQL copy, and Azure Portal links.
- Shared feedback includes known-layout skeletons, loading, inline errors and summary, status-region/toast, empty state, confirmation, and retry action.
- The diagnostic panel exposes only `test.run_id`, `scenario.id`, `order.id`, `event.id`, `trace.id`, timestamps, and copy actions; it is authorized non-production content only.

## Browser architecture and reliability

- Use the OpenAPI-generated client or a focused API-client project; never share EF/domain entities with the browser.
- Keep state local unless navigation survival requires a small scoped service for authenticated user, active submission, or Observability Lab run state. Do not add a general state-management framework without a demonstrated need.
- Centralize bearer-token, W3C trace-context, test-run-header, idempotency-key, timeout/cancellation, Problem Details parsing, and safe retry behavior in handlers/services.
- Retry bounded GET requests only. Never automatically replay POST unless the exact same durable idempotency key is preserved.
- Cancel navigation-abandoned calls and prevent stale responses from replacing newer state.
- Use UTC on the wire and render local time with visible timezone where ambiguity matters.
- Validate environment-specific API/TestSupport URLs. A browser build contains no client secret.
- Host the WASM application on Azure Static Web Apps. IaC and runbooks must define custom domain/TLS, cache headers, compression, SPA fallback, immutable assets, promotion, and rollback. Client route rules are defense in depth: the API authorizes every sensitive request.

## Authentication and security

Use Microsoft Entra authorization-code flow with PKCE through the supported Blazor WebAssembly library. The SPA is a public client and never holds a secret; the frontend and API use separate registrations/scopes as required. Tokens are managed only by the selected supported authentication library, never by a custom local-storage implementation.

Business API authorization and the `TestOperator` role are separate. Observability Lab access requires TestOperator. Configure exact per-environment CORS origins, permitted methods/headers, exposed correlation headers, and no wildcard credentials. Add a Blazor-compatible CSP plus frame-ancestors, referrer, MIME-sniffing, and permissions policies after validating current official guidance. Encode untrusted values and never render server error details as markup.

Playwright uses dedicated non-production OrderUser and TestOperator accounts. Their credentials are protected environment secrets, rotated, constrained by deliberate Conditional Access, and never production accounts. Do not use ROPC. Setup may create short-lived storage state, but it is ignored and never committed or uploaded.

## Accessibility acceptance criteria

The target is WCAG 2.2 AA. The application has semantic landmarks/headings, associated labels/help/errors, logical tab order, visible focus, skip link, correct focus movement, adequate target size and contrast, no color-only status, 200–400% zoom/reflow, narrow responsive layouts, reduced-motion/contrast/text-scaling/high-contrast support where practical, and restrained `aria-live` announcements. Duplicate submission is disabled visually but server idempotency remains authoritative. Empty/error states name the safe next action, and destructive Lab actions state scope/duration and require confirmation.

## Verification gate

`tests/CloudOrders.Web.Tests` must cover form validation and focus, idempotent submission state, Problem Details mapping, authorization rendering, order route, diagnostic gating, loading/empty/error states, and reduced-motion markup. Service tests cover typed-client mapping, cancellation, bounded GET retry, and preservation of POST idempotency keys.

Release builds, component/unit tests, keyboard and screen-reader smoke tests, automated accessibility checks, responsive/cross-browser Playwright coverage, and a deployed API create/track journey are required before this contract is considered delivered. Evidence belongs in `docs/evidence/sprint-10/` and its later release gates.
