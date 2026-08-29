using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CloudOrders.Api.Identity;

public sealed class ExternalIdentityOptions
{
    public const string SectionName = "ExternalIdentity";

    public required string Authority { get; init; }
    public required string ValidIssuer { get; init; }
    public required string TenantId { get; init; }
    public required string Audience { get; init; }
    public required string[] AllowedClientIds { get; init; }
}

public sealed class ExternalIdentityOptionsValidator : IValidateOptions<ExternalIdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, ExternalIdentityOptions options)
    {
        var failures = new List<string>();
        ValidateHttpsUri(options.Authority, nameof(options.Authority), failures);
        ValidateHttpsUri(options.ValidIssuer, nameof(options.ValidIssuer), failures);
        ValidateGuid(options.TenantId, nameof(options.TenantId), failures);
        ValidateGuid(options.Audience, nameof(options.Audience), failures);

        if (Guid.TryParseExact(options.TenantId, "D", out var tenantId)
            && Uri.TryCreate(options.ValidIssuer, UriKind.Absolute, out var issuer)
            && !issuer.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, tenantId.ToString("D"), StringComparison.Ordinal)))
        {
            failures.Add("ValidIssuer must contain the exact TenantId path segment.");
        }

        if (options.AllowedClientIds is not { Length: > 0 })
        {
            failures.Add("AllowedClientIds must contain at least one client ID.");
        }
        else
        {
            var clients = new HashSet<Guid>();
            foreach (var clientId in options.AllowedClientIds)
            {
                ValidateGuid(clientId, "AllowedClientIds", failures);
                if (Guid.TryParseExact(clientId, "D", out var parsedClientId) && !clients.Add(parsedClientId))
                {
                    failures.Add("AllowedClientIds must be distinct.");
                }
            }
        }

        return failures.Count is 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateHttpsUri(string? value, string name, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.EndsWith('/')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{name} must be an absolute HTTPS URI without whitespace or a trailing slash.");
        }
    }

    private static void ValidateGuid(string? value, string name, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || !Guid.TryParseExact(value, "D", out _))
        {
            failures.Add($"{name} must be a GUID in D format without whitespace.");
        }
    }
}
