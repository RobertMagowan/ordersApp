# Frontend Sprint Replan Design

## Purpose

Give the standalone .NET 10 Blazor WebAssembly application explicit delivery boundaries. The previous roadmap combined API containerization, Entra authentication, Static Web Apps hosting, all user workflows, accessibility, and browser testing in one sprint. That scope was too large to remain manually testable and deployable as a single sprint.

## Approaches Considered

1. Keep one large frontend sprint. This preserves numbering but creates a long integration phase with weak intermediate gates.
2. Retain the web platform sprint and add three focused frontend sprints. This is the selected approach because each increment has a user-visible outcome and an Azure deployment gate.
3. Build each frontend page alongside its backend endpoint. This shortens individual feedback loops but would substantially reorder the approved persistence, messaging, and Azure dependency sequence.

## Selected Sprint Boundaries

- **Sprint 7 — Web delivery and authentication foundation:** immutable API deployment, standalone WASM host, typed API client, Entra authorization-code/PKCE integration, Static Web Apps linking, and secure edge configuration. The deployed shell must sign in and make an authorized API call.
- **Sprint 8 — Frontend shell and design system:** dispatch-control visual language, responsive layout, navigation, shared form and feedback components, business-status route, accessibility baseline, and bUnit coverage. The deployed shell must work with keyboard navigation and representative narrow and wide viewports.
- **Sprint 9 — Order workflows:** home/lookup, create order, order details, customer history, idempotent submission, polling, URL-preserved pagination, and complete loading/empty/error states. A user must create, find, and track an order through the deployed API.
- **Sprint 10 — Frontend quality and release integration:** authentication edge states, cancellation and safe retry behavior, WCAG 2.2 AA evidence, responsive/cross-browser Playwright, browser telemetry, bundle budgets, stale-asset/API compatibility, deployment, and rollback verification.

The non-production Observability Lab remains with TestSupport in Sprint 11 because its controls and safety model depend on that API. CI/CD and production readiness move to Sprints 12 and 13.

## Constraints and Verification

Section 19 of `CLOUDORDERS_HANDOFF.md` remains the frontend design authority. The browser contains no client secret, API authorization remains authoritative, POST retries preserve the same durable idempotency key, and infrastructure states remain hidden from ordinary users. Each sprint ends with Release builds, focused automated tests, a manual browser journey, an Azure development deployment, and retained evidence.

## Effort Model

For one focused developer, the three new frontend sprints add approximately 13–20 working days. The complete roadmap is approximately 59–91 working days, excluding external approval, tenant-access, and unexpected Azure service delays.
