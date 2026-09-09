# Final review-fix report

## Scope

- Added an immediately-pre-merge live pull-request eligibility check to the nonproduction auto-merge workflow.
- Corrected `AGENTS.md` so it says development/test deployments run automatically after protected merges.
- Added architecture assertions for the live query and event SHA comparison.

## RED evidence

Command:

```powershell
dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter "FullyQualifiedName~NonProductionAutoMergeWorkflowIsNarrowlyGuarded" --no-restore
```

Result: failed as expected because the workflow did not contain the required `gh pr view` live-state query (`Failed: 1, Passed: 0, Total: 1`).

## GREEN evidence

Focused command:

```powershell
dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --filter "FullyQualifiedName~NonProductionAutoMergeWorkflowIsNarrowlyGuarded|FullyQualifiedName~NonproductionPromotionDocumentation" --no-restore
```

Result: passed (`Failed: 0, Passed: 2, Total: 2`).

Full architecture command:

```powershell
dotnet test tests/CloudOrders.ArchitectureTests/CloudOrders.ArchitectureTests.csproj --no-restore
```

Result: passed (`Failed: 0, Passed: 34, Total: 34`).

## Guard behavior

Immediately before requesting auto-merge, the workflow queries the current PR with `gh pr view` for `baseRefName`, `headRefName`, `isDraft`, `headRepository`, and `headRefOid`. It exits non-zero if the query fails, the PR is draft, the head repository differs from the event repository, any current base/head ref differs from the event, or the current head OID differs from the triggering event SHA. Only then does it invoke merge-commit auto-merge.

## Concerns

The guard relies on the `jq` executable bundled on GitHub-hosted Ubuntu runners, which is standard there. No production deployment workflow, event type, permissions, repository settings, or unrelated files were changed.
