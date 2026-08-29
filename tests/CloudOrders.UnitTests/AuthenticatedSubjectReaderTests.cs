using System.Security.Claims;
using CloudOrders.Api.Identity;

namespace CloudOrders.UnitTests;

public sealed class AuthenticatedSubjectReaderTests
{
    [Fact]
    public void TryReadReturnsVerifiedEmailForOneVerifiedEmailClaim()
    {
        var oid = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://tenant.ciamlogin.com/11111111-1111-1111-1111-111111111111/v2.0"),
            new Claim("oid", oid.ToString("D")),
            new Claim("email", "customer@example.test"),
            new Claim("email_verified", "true")
        ], "Bearer"));

        var result = AuthenticatedSubjectReader.TryRead(principal, out var subject);

        Assert.True(result);
        Assert.Equal(oid, subject.ObjectId);
        Assert.Equal("customer@example.test", subject.VerifiedContactEmail);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void TryReadRejectsMalformedOid(string oid)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("iss", "https://tenant.example/v2.0"), new Claim("oid", oid)], "Bearer"));

        Assert.False(AuthenticatedSubjectReader.TryRead(principal, out _));
    }

    [Fact]
    public void TryReadIgnoresUnverifiedOrMultipleEmails()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://tenant.example/v2.0"),
            new Claim("oid", Guid.NewGuid().ToString()),
            new Claim("email", "first@example.test"),
            new Claim("email", "second@example.test"),
            new Claim("email_verified", "true")
        ], "Bearer"));

        Assert.True(AuthenticatedSubjectReader.TryRead(principal, out var subject));
        Assert.Null(subject.VerifiedContactEmail);
    }
}
