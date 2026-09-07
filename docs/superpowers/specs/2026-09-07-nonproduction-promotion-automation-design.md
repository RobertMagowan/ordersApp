# Nonproduction Promotion Automation Design

## Goal

Remove routine human merge and environment-approval pauses from the
nonproduction path while retaining protected branches, required checks, and
conversation resolution.

## Scope

This design applies only to these authorised pull-request paths:

- feature/* to development
- development to test

It does not change test to master, production deployment behaviour, branch
protection, or the prohibition on direct pushes.

## Design

Enable GitHub auto-merge at repository level. Add a least-privilege workflow
that enables merge-commit auto-merge only when the pull request is ready,
originates in this repository, and matches one of the two authorised
nonproduction paths. It must not approve reviews, dismiss reviews, resolve
conversations, or merge a pull request directly.

GitHub branch protection remains the merge authority. Auto-merge waits until
the existing required status checks are current and all review conversations
are resolved. A review comment therefore prevents merging until an agent or
developer has addressed it and the reviewer marks the conversation resolved.
Review comments are assessed individually: valid blockers are fixed; useful
non-blockers may be fixed when proportionate; incorrect or overly broad
comments are answered with evidence and left unresolved only if the reviewer
chooses not to close them.

Remove required-reviewer environment protection from development and test
only. Their branch restrictions remain. Consequently, a successful protected
branch merge triggers deployment without a GitHub environment approval. A
failed deployment remains failed: it is diagnosed, fixed on a new feature/*
branch, reviewed through the same gates, and then rerun by the normal
merge-triggered workflow. The deployment workflow must not blindly retry
Azure mutations.

## Safety and Verification

The auto-merge workflow uses pull-requests: write and contents: write,
checks the same-repository head before enabling auto-merge, and uses merge
commits to preserve repository policy. Architecture tests assert the
permitted source/base pairs and reject production or forked pull requests.
GitHub API verification confirms auto-merge is enabled and that
development/test have no reviewer gate while production's configuration is
unchanged.
