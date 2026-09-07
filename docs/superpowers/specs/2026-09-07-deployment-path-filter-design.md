# Deployment path filter design

## Purpose

Prevent delivery-state and evidence updates from creating an application revision solely because they are committed to the same repository. These updates remain subject to pull-request policy, CI, and sprint-delivery contract validation.

## Decision

The deployment workflow will first classify the change range. It runs the existing Azure preview, provisioning, migration, image, and application-deployment jobs only when the range contains a deployable path. A delivery-only range completes successfully with an explicit skipped-deployment summary and performs no Azure write.

Deployable paths include application source, tests that affect the shipped solution, database migrations, infrastructure, container build inputs, local/runtime configuration, and `.github/workflows/deploy.yml`. Documentation, `delivery/`, sprint evidence, operational test scripts, and non-deployment GitHub workflows are delivery-only unless another deployable path is present.

## Workflow behavior

`deploy.yml` remains a protected-branch push and manual-dispatch workflow; it does not gain a pull-request trigger. Pull requests continue to use the existing CI, Bicep, branch-policy, and sprint-delivery validation workflows without Azure credentials or deployment environments.

For a protected-branch push, a new read-only classifier checks out the exact pushed commit with full history, fetches the event `before` commit when necessary, and runs `git diff --name-only <before> <after>`. It classifies the range as deployable when any changed path matches the deployable set. A zero, unavailable, malformed, or unresolvable `before` commit is classified as deployable. Manual dispatch is also deployable by default because it has no trustworthy change range.

Every Azure-mutating job depends on the classifier and requires `deployable == true`: foundation and release preview, image publication, Azure SQL preview/provision, normal SQL migration, application deployment, and the Sprint 4A E1 migration-only job. The promotion-ref validation remains read-only and always runs. A non-deployable result has a standalone summary job with no environment, Azure login, or OIDC permission.

The skipped summary records the range and reason but does not expose secrets or identity settings. Production remains unchanged: no new production action is introduced, and existing environment approvals remain required for deployable releases.

## Acceptance criteria

1. A delivery-only protected-branch push does not invoke Azure login, Bicep deployment, image publication, SQL migration (including E1-only), or application deployment.
2. A source, migration, infrastructure, or deployment-workflow change follows the existing deployment path.
3. A mixed change follows the deployment path.
4. Manual dispatch, first-push, unavailable-`before`, and unresolvable-history cases fail closed to deployment.
5. The workflow contract test covers delivery-only, source, migration, infrastructure, deploy-workflow, mixed, E1-only, and indeterminate-range classifications without Azure credentials.
6. Pull requests do not run `deploy.yml`, and the delivery-only summary does not request a GitHub environment approval.

## Scope and risks

This change does not alter application code, Azure resources, branch policy, identity configuration, or migration semantics. It only adds a deterministic gate ahead of the existing deployment jobs. The fail-closed classifier protects against an omitted path category; any uncertainty causes the established deployment workflow to run.
