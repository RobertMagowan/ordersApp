using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace CloudOrders.IntegrationTests;

internal sealed class SignedJwtFactory : IDisposable
{
    internal const string TenantId = "11111111-1111-1111-1111-111111111111";
    internal const string Audience = "22222222-2222-2222-2222-222222222222";
    internal const string ClientId = "33333333-3333-3333-3333-333333333333";
    internal const string Issuer = "https://11111111-1111-1111-1111-111111111111.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0";
    private readonly RSA rsa = RSA.Create(2048);

    internal RsaSecurityKey PublicKey => new(rsa.ExportParameters(false));

    internal string CreateToken(
        string? issuer = null,
        string? tenantId = null,
        string? audience = null,
        string? clientId = null,
        string? oid = null,
        IEnumerable<string>? objectIds = null,
        string? scope = "Orders.Read",
        string[]? roles = null,
        IEnumerable<System.Security.Claims.Claim>? additionalClaims = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        bool signed = true)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("tid", tenantId ?? TenantId),
            new("azp", clientId ?? ClientId)
        };
        foreach (var objectId in objectIds ?? (oid is null ? [] : [oid])) claims.Add(new("oid", objectId));
        if (scope is not null) claims.Add(new("scp", scope));
        foreach (var role in roles ?? []) claims.Add(new("roles", role));
        claims.AddRange(additionalClaims ?? []);

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: signed ? new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256) : null);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal string CreateUnsignedToken(string oid) => CreateToken(oid: oid, signed: false);

    internal string CreateAppOnlyToken() => CreateToken(
        oid: null,
        scope: null,
        roles: ["user.admin"],
        additionalClaims: [new("idtyp", "app")]);

    public void Dispose() => rsa.Dispose();
}
