using System.Security.Claims;
using CloudOrders.Api.Identity;
using Microsoft.AspNetCore.Authorization;

namespace CloudOrders.UnitTests;

public sealed class CustomerResourceAuthorizationHandlerTests
{
    [Fact]
    public async Task CustomerCanAccessOnlyTheirOwnProfile()
    {
        var owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var requirement = new CustomerResourceRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity([], "test")),
            new CustomerResource(owner, other));

        await new CustomerResourceAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AdminCanAccessAnotherProfile()
    {
        var requirement = new CustomerResourceRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", CloudOrdersPermissions.AdminRole)], "test")),
            new CustomerResource(Guid.NewGuid(), Guid.NewGuid()));

        await new CustomerResourceAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}
