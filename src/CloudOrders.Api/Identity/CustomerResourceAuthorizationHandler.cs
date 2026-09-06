using Microsoft.AspNetCore.Authorization;

namespace CloudOrders.Api.Identity;

public sealed record CustomerResource(Guid ActorCustomerProfileId, Guid TargetCustomerProfileId);

public sealed class CustomerResourceRequirement : IAuthorizationRequirement;

public sealed class CustomerResourceAuthorizationHandler
    : AuthorizationHandler<CustomerResourceRequirement, CustomerResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerResourceRequirement requirement,
        CustomerResource resource)
    {
        var isAdmin = context.User.FindAll("roles")
            .Any(claim => string.Equals(claim.Value, CloudOrdersPermissions.AdminRole, StringComparison.Ordinal));
        if (resource.ActorCustomerProfileId == resource.TargetCustomerProfileId || isAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
