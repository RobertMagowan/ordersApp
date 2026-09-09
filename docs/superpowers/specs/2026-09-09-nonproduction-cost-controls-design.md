# Non-production Cost Controls

## Decision

Development and test remain separate Azure resource groups, Container Apps environments, SQL logical servers, and Azure SQL databases. The Azure SQL free offer and shared-server design are out of scope.

## Behaviour

- Development and test parameter overlays set the API Container App minimum replicas to zero. The production overlay explicitly retains one. An incoming request, deployment smoke test, or QA request starts a non-production replica on demand.
- Deployment smoke testing retains bounded retries so cold API starts and Azure SQL serverless resume do not cause false failures.
- Development and test Azure SQL databases remain General Purpose Serverless (`GP_S_Gen5_1`, minimum capacity `0.5`) but reduce their inactivity auto-pause delay from 60 to 15 minutes.
- Production behaviour and the protected promotion model are unchanged.

## Validation

Bicep validation must prove the composition root and non-production parameter overlays compile. The deployment workflow must be inspected to confirm its candidate revision and health checks retry long enough for on-demand startup. Development deployment then verifies live and ready health after an idle start.
