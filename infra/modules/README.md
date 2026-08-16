# Infrastructure Modules

These local modules provide the CloudOrders composition boundary around pinned Azure Verified Modules. `main.bicep` owns deployment parameters and cross-resource dependencies; each module owns one Azure capability.

The Container App module deliberately does not create the registry-scoped `AcrPull` assignment. That assignment is created by the composition root after the system-assigned identity exists, matching the two-phase bootstrap deployment used by GitHub Actions.
