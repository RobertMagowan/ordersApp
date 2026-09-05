using System.Security.Claims;
using CloudOrders.Application.Identity;

namespace CloudOrders.Api.Identity;

public static class AuthenticatedSubjectReader
{
    public static bool TryRead(ClaimsPrincipal principal, out AuthenticatedSubject subject)
    {
        subject = default!;
        var issuer = SingleValue(principal, "iss");
        var oid = SingleValue(principal, "oid");
        if (issuer is null || oid is null || !Guid.TryParse(oid, out var objectId))
        {
            return false;
        }

        var emails = principal.FindAll("email").Select(claim => claim.Value).ToArray();
        var verified = string.Equals(SingleValue(principal, "email_verified"), "true", StringComparison.Ordinal);
        var verifiedEmail = verified && emails.Length is 1 ? emails[0] : null;
        subject = new AuthenticatedSubject(issuer, objectId, verifiedEmail);
        return true;
    }

    private static string? SingleValue(ClaimsPrincipal principal, string type)
    {
        var values = principal.FindAll(type).Select(claim => claim.Value).ToArray();
        return values.Length is 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;
    }
}
