# Release Migration Manifest Design

## Goal

Make each non-production database migration an explicit, immutable part of its release so a successful API deployment cannot silently omit its required schema change.

## Chosen approach

Each migration release adds a versioned manifest under `ops/releases/`. The manifest declares a release identifier, exactly one EF migration, permitted environments, whether the API is deployed, and any named precondition. The workflow validates the schema and selects the manifest from the immutable release commit.

For a migration release, the deployment workflow will:

1. Validate the manifest before Azure mutation.
2. Build the immutable migration image from the same commit as the API image.
3. Start the existing migration Container App Job with exactly `--migration <name>`.
4. Poll the execution to `Succeeded`, fail on `Failed` or timeout, and confirm the exact image and arguments.
5. Verify the named migration is present in `__EFMigrationsHistory` before deploying the API.

Ordinary code-only releases have no migration manifest change and skip the migration execution. Existing Sprint 4 release controls remain valid only as compatibility data; the workflow no longer embeds Sprint-specific migration names.

## Safety and scope

This retains the current `feature/* → development → test → master` promotion model, immutable image deployment, OIDC identities, Azure SQL preview, and API smoke tests. It does not auto-apply all pending migrations, change production behaviour, grant permissions, or introduce secrets. The first manifest will declare `AddOutboxLeasing` for `development` and `test`; production remains excluded.

## Validation

Workflow contract tests will prove malformed or undeclared manifests fail before Azure mutation, code-only releases skip the migration job, and a valid manifest passes its exact migration name through to the Job. Development validation will record the live API revision, migration execution, `__EFMigrationsHistory` entry, and outbox lease columns before Task 1 may close.
