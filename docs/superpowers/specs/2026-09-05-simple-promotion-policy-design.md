# Simple Promotion Policy Design

## Purpose

Implement the repository promotion model without post-merge ancestry checks, synthetic statuses, or self-locking workflow files.

## Promotion Flow

1. A `feature/*` branch opens a draft pull request to `development`.
2. The feature is completed, updated from `development` when necessary, locally verified, reviewed, and merged with a merge commit.
3. Each merged feature is deployed and tested in development. When the sprint is ready, all sprint features are tested together there.
4. A single `development` to `test` pull request promotes that tested sprint. Its merge deploys test for QA.
5. A `test` to `master` pull request is the only production promotion path. Production deployment remains subject to explicit authorisation.

## Enforcement

The ordinary `pull_request` workflow validates only the allowed source/base pairs:

- `feature/*` to `development`
- `development` to `test`
- `test` to `master`

Protected branches require this validation and normal CI, and disallow direct pushes, force pushes, and deletion. GitHub permits merge commits only. A normal GitHub pull-request check is intentionally sufficient because the sole developer reviews every pull request.

## Content Integrity

Promotion branches receive no independent product changes. A `development` to `test` pull request therefore presents the tested development content for review; resolving a conflict that changes product content is not permitted and must be returned to development. Merge commits may produce different commit IDs, but the promoted code tree is the reviewed development tree.

## Non-Goals

This policy does not attempt to create cryptographic workflow immutability, inspect merge parents after merge, publish synthetic check runs, or perform production deployment automatically.
