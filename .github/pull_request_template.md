## Sprint gate

- Sprint:
- Related plan/issue:

## Delivery state and evidence

- [ ] Delivery state impact is recorded, or this change has no delivery-state impact.
- [ ] Evidence impact is recorded, including any evidence made stale or superseded.
- Gate status: `pending` / `pass` / `not applicable` (explain exceptions below).

This template records review information only; it does not advance lifecycle state.

## Verification

- [ ] `dotnet format --verify-no-changes`
- [ ] `dotnet build --configuration Release`
- [ ] `dotnet test --configuration Release`
- [ ] Manual test or deployment evidence is attached.

## Change impact

- [ ] No Azure, schema, security, or configuration changes.
- [ ] Azure/IaC changes include validation and what-if evidence.
- [ ] Migration, rollback, and operational notes are documented.
