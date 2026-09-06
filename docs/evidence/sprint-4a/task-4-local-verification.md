# Sprint 4A Task 4 local verification

Date: 2026-09-06
Commit: `58c5c6c`

## Automated checks

| Check | Outcome |
| --- | --- |
| `dotnet format --verify-no-changes --no-restore` | Passed |
| `dotnet build CloudOrders.slnx --configuration Release --no-restore` | Passed with zero warnings and zero errors |
| Focused ownership integration tests | Passed: 5 tests |
| Existing SQL order integration tests | Passed: 17 tests |
| Policy and JWT integration tests | Passed: 25 tests |
| Unit tests | Passed: 19 tests |
| Architecture tests | Passed: 26 tests |

## Developer verification

The SQL-backed verification used signed test JWTs and two distinct seeded customer profiles. It proved that a customer can create and retrieve only its own order; foreign and absent resources return equivalent safe `404 resource_not_found` responses; an exact `user.admin` role can access an existing foreign resource; and a token without that role cannot replay a foreign request.

Direct SQL assertions confirm an API-created order stores the target `CustomerProfileId`, and its idempotency row stores the legacy profile subject plus actor and target profile IDs. The request hash is calculated from canonical actor, target, customer reference, SKU, and quantity values.

Audit unit verification captures the structured logging state and permits only action, result, profile/order IDs, capability, trace ID, and environment. It rejects email, customer reference, product, request, and bearer-token values.
