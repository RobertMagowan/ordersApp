# External ID recovery

Use one of the two emergency Global Administrators only for an External ID control-plane outage, lockout, compromised owner, or incorrect application configuration. Record the incident, acting administrator, reason, and recovery evidence in the protected evidence store, then restore routine ownership and remove emergency access when the incident is closed.

Recovery checks are: verify API registration has no Graph permission; verify the public client has no secret and only its local redirect; verify the user flow and workforce provider configuration; verify the API enterprise application does not require assignment; and verify the product administrator role is assigned to the External ID user object. Do not place recovery identifiers, client secrets, token material, or browser state in repository files.
