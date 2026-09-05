# External ID role operations

Only the routine Cloud Application Administrator, or an emergency Global Administrator during recovery, may grant or revoke the sole Sprint 4 product role. Confirm that the target is the federated or OTP user object in the External ID tenant, not the workforce account. Grant the exact `user.admin` application role only when the approved product-access record permits it; it does not grant any directory capability.

After each change, acquire a fresh interactive API token, verify the intended access result, and record the operator, target External ID object, timestamp, token-issued-after timestamp, and sanitized outcome in the protected evidence store. Do not retain token text, decoded claim dumps, email addresses, or the workforce object ID. A revoked role remains effective only until previously issued tokens expire; do not infer revocation from a stale token.
