using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CloudOrders.Api.Identity;

public sealed class CloudOrdersAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication is required.",
                extensions: ProblemExtensions(context, "authentication_required")).ExecuteAsync(context);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The request is not authorized.",
                extensions: ProblemExtensions(context, "authorization_forbidden")).ExecuteAsync(context);
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static Dictionary<string, object?> ProblemExtensions(HttpContext context, string errorCode) =>
        new Dictionary<string, object?> { ["errorCode"] = errorCode, ["traceId"] = context.TraceIdentifier };
}
