using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudOrders.Api.Health;
using CloudOrders.Api.Identity;
using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using CloudOrders.Infrastructure.Identity;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var sqlConnectionString = builder.Configuration.GetConnectionString("CloudOrders");
var connectionCanBeDeferred = builder.Environment.IsDevelopment()
    || builder.Environment.IsEnvironment("Test")
    || builder.Environment.IsEnvironment("Testing");
if (string.IsNullOrWhiteSpace(sqlConnectionString) && !connectionCanBeDeferred)
{
    throw new InvalidOperationException(
        "SQL persistence requires configuration key ConnectionStrings:CloudOrders.");
}

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<ExternalIdentityOptions>()
    .Bind(builder.Configuration.GetSection(ExternalIdentityOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ExternalIdentityOptions>, ExternalIdentityOptionsValidator>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<ExternalIdentityOptions>>((options, externalIdentity) =>
    {
        var identity = externalIdentity.Value;
        options.Authority = identity.Authority;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = identity.ValidIssuer,
            ValidateAudience = true,
            ValidAudience = identity.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "roles"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var principal = context.Principal!;
                if (!HasSingleExactClaim(principal, "tid", identity.TenantId)
                    || !HasSingleAllowedClient(principal, identity.AllowedClientIds)
                    || !AuthenticatedSubjectReader.TryRead(principal, out _)
                    || !HasDelegatedScope(principal))
                {
                    context.Fail("The token cannot establish an authorized user subject.");
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrdersRead", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        HasScope(context.User, CloudOrdersPermissions.ReadScope) && HasOnlyKnownRoles(context.User)));
    options.AddPolicy("OrdersWrite", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        HasScope(context.User, CloudOrdersPermissions.WriteScope) && HasOnlyKnownRoles(context.User)));
});
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, CloudOrdersAuthorizationResultHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddDbContextFactory<CloudOrdersDbContext>((serviceProvider, options) =>
{
    var configuredConnectionString = serviceProvider
        .GetRequiredService<IConfiguration>()
        .GetConnectionString("CloudOrders");
    if (!string.IsNullOrWhiteSpace(configuredConnectionString))
    {
        options.UseSqlServer(configuredConnectionString);
    }
});
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddScoped<IIdempotentOrderStore, SqlIdempotentOrderStore>();
builder.Services.AddSingleton<ISubjectIdProvider, LocalDevelopmentSubjectIdProvider>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<SqlReadinessHealthCheck>("sql", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var statusCode = exception is BadHttpRequestException badRequestException
        ? badRequestException.StatusCode
        : StatusCodes.Status500InternalServerError;

    await Results.Problem(
        statusCode: statusCode,
        title: statusCode is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType
            ? "The request is invalid."
            : "An unexpected error occurred.",
        extensions: ProblemExtensions(context, statusCode is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType
            ? "invalid_request"
            : "internal_error"))
        .ExecuteAsync(context);
}));
app.UseStatusCodePages(async statusCodeContext =>
{
    var context = statusCodeContext.HttpContext;
    var statusCode = context.Response.StatusCode;
    var isRequestError = statusCode is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType;

    await Results.Problem(
        statusCode: statusCode,
        title: isRequestError ? "The request is invalid." : "The requested operation failed.",
        extensions: ProblemExtensions(context, isRequestError ? "invalid_request" : "http_error"))
        .ExecuteAsync(context);
});

app.MapGet("/health/live", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("LiveHealth")
    .WithTags("Health");

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
})
    .WithName("ReadyHealth")
    .WithTags("Health");

app.MapPost("/api/v1/orders", async (
        CreateOrderRequest request,
        HttpContext httpContext,
        CreateOrderHandler handler,
        CancellationToken cancellationToken) =>
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            errors["customerReference"] = ["Customer reference is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ProductSku))
        {
            errors["productSku"] = ["Product SKU is required."];
        }

        if (errors.Count > 0)
        {
            return OrderValidationProblem(httpContext, errors);
        }

        if (!TryParseIdempotencyKey(httpContext, out var idempotencyKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A valid Idempotency-Key UUID is required.",
                extensions: ProblemExtensions(httpContext, "invalid_idempotency_key"));
        }

        var traceParent = Activity.Current?.Id;

        var result = await handler.Handle(
            new CreateOrderCommand(request.CustomerReference!, request.ProductSku!, request.Quantity),
            idempotencyKey,
            traceParent,
            cancellationToken);

        if (result.Kind is CreateOrderResultKind.ValidationError)
        {
            return OrderValidationProblem(
                httpContext,
                new Dictionary<string, string[]>
                {
                    ["request"] = [result.ErrorMessage ?? "The request is invalid."]
                });
        }

        if (result.Kind is CreateOrderResultKind.Conflict)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The idempotency key conflicts with an earlier request.",
                extensions: ProblemExtensions(httpContext, result.ErrorCode ?? "idempotency_conflict"));
        }

        if (result.Kind is CreateOrderResultKind.Replayed)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Ok(result.Response);
        }

        return Results.Created($"/api/v1/orders/{result.Response!.Id}", result.Response);
    })
    .WithName("CreateOrder")
    .WithTags("Orders")
    .RequireAuthorization("OrdersWrite");

app.MapGet("/api/v1/orders/{orderId:guid}", async (
        Guid orderId,
        GetOrderHandler handler,
        CancellationToken cancellationToken) =>
    {
        var response = await handler.Handle(orderId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    })
    .WithName("GetOrder")
    .WithTags("Orders")
    .RequireAuthorization("OrdersRead");

app.Run();

static IResult OrderValidationProblem(HttpContext context, IDictionary<string, string[]> errors) =>
    Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The order request is invalid.",
        extensions: new Dictionary<string, object?>(ProblemExtensions(context, "validation_error"))
        {
            ["errors"] = errors
        });

static bool TryParseIdempotencyKey(HttpContext context, out Guid idempotencyKey)
{
    idempotencyKey = Guid.Empty;
    var values = context.Request.Headers["Idempotency-Key"];
    return values.Count is 1 && Guid.TryParse(values[0], out idempotencyKey);
}

static IDictionary<string, object?> ProblemExtensions(HttpContext context, string errorCode) =>
    new Dictionary<string, object?>
    {
        ["errorCode"] = errorCode,
        ["traceId"] = context.TraceIdentifier
    };

static bool HasSingleExactClaim(System.Security.Claims.ClaimsPrincipal principal, string type, string expected) =>
    principal.FindAll(type).Select(claim => claim.Value).ToArray() is [var value]
    && string.Equals(value, expected, StringComparison.Ordinal);

static bool HasSingleAllowedClient(System.Security.Claims.ClaimsPrincipal principal, IEnumerable<string> allowedClientIds) =>
    principal.FindAll("azp").Select(claim => claim.Value).ToArray() is [var clientId]
    && allowedClientIds.Contains(clientId, StringComparer.Ordinal);

static bool HasDelegatedScope(System.Security.Claims.ClaimsPrincipal principal) =>
    principal.FindAll("scp").Select(claim => claim.Value).Any(value => !string.IsNullOrWhiteSpace(value));

static bool HasScope(System.Security.Claims.ClaimsPrincipal principal, string requiredScope) =>
    principal.FindAll("scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));

static bool HasOnlyKnownRoles(System.Security.Claims.ClaimsPrincipal principal) =>
    principal.FindAll("roles").All(role => string.Equals(role.Value, CloudOrdersPermissions.AdminRole, StringComparison.Ordinal));

public partial class Program;
