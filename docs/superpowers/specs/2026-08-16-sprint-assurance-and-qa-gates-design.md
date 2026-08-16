# Sprint Assurance and QA Gates Design

## Purpose

Split the next oversized delivery phase and make every remaining CloudOrders sprint testable, reviewable, and promotable through Azure environments. The standard applies to all work after the already-delivered Sprint 1 baseline.

## Delivery Model

Each sprint has four ordered gates:

1. Development work is completed on `feature/*` with focused commits and automated tests.
2. A separate high-capability verification agent spends three working days testing the deployed `development` release, using the evidence approach suited to the changed technology.
3. A reviewed `development` → `test` PR deploys the immutable release to the Azure `test` environment. A separate QA-only agent tests there without editing code.
4. Only a clean QA result permits a `test` → `master` PR and production promotion. A defect is fixed only on a new `feature/*` branch, then follows the same development and test cycle again.

The implementation agent must not self-certify the development verification or test QA gates. Verification and QA agents record commands, environment/release identifiers, outcomes, defects, and evidence locations.

## Test Selection

The sprint test plan is selected before implementation from the risk and technology being changed:

- Domain/API changes use unit, integration, contract, concurrency, negative-authorization, and deployed HTTP tests.
- SQL, messaging, and migration changes use Testcontainers or emulators, migration/rollback checks, failure/recovery drills, and Azure service-flow tests.
- IaC and CI/CD changes use lint/build, parameter validation, what-if, least-privilege negative checks, workflow evidence, and deployed smoke/rollback tests.
- Frontend changes use bUnit, accessibility, Playwright, responsive/cross-browser, and real user-journey tests.

Every sprint retains its existing automated verification and Azure development deploy gate; the new verification phases are additional acceptance gates, not replacements.

## Sprint 2 Boundary

Sprint 2 becomes **Workflow, Contract, and Test-Environment Assurance**. It has 2–3 development days, three development-verification days, and 1–2 QA days, for 6–8 working days before defect remediation. It upgrades and commit-SHA-pins Actions, removes insecure deployment behavior, completes workflow safeguards and summaries, versions handoff section 19 and sections 25–35 under `docs/contracts/`, and provisions the minimum protected Azure `test` environment.

Its development verification proves CI, branch policy, deployment, rollback, contract-pack traceability, and test-environment readiness. Its QA pass verifies the `development` → `test` promotion, deployed smoke path, evidence retention, and that a QA finding follows the feature-branch correction loop.

## Effort and Evidence

For every remaining sprint after Sprint 2, keep the existing development estimate and add three working days for development verification plus 1–2 working days for independent QA. Defect remediation is separately scoped from the affected feature branch and re-estimated before work begins. Sprint evidence is retained under `docs/evidence/sprint-<number>/` and includes the deployment URL/run, immutable artifact or release identifier, test results, QA outcome, and any re-test record.

## Constraints

The `test` environment is the staging-equivalent Azure environment. It must be provisioned and protected before the first independent QA pass. The established promotion sequence remains `feature/*` → `development` → `test` → `master`; all protected-branch merges use PRs and required checks. Production resources are unchanged until their existing explicit approval gates are met.
