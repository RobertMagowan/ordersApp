using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudOrders.Api.Health;
using CloudOrders.Api.Identity;
using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Identity;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
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
        options.IncludeErrorDetails = false;
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
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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
builder.Services.AddScoped<ICustomerProfileStore, SqlCustomerProfileStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuthorizationAuditSink>(serviceProvider =>
    new LoggerAuthorizationAuditSink(
        serviceProvider.GetRequiredService<ILogger<LoggerAuthorizationAuditSink>>(),
        serviceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName));
builder.Services.AddScoped<CurrentCustomerProfileAccessor>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerResourceAuthorizationHandler>();
builder.Services.AddSingleton<ICustomerReferenceGenerator, CustomerReferenceGenerator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<SqlReadinessHealthCheck>("sql", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi()
        .RequireAuthorization();
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
    .WithTags("Health")
    .AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
})
    .WithName("ReadyHealth")
    .WithTags("Health")
    .AllowAnonymous();

app.MapPost("/api/v1/orders", async (
        CreateOrderRequest request,
        HttpContext httpContext,
        CreateOrderHandler handler,
        CurrentCustomerProfileAccessor currentCustomer,
        ICustomerProfileStore customerProfiles,
        IAuthorizationService authorizationService,
        IAuthorizationAuditSink auditSink,
        IHostEnvironment hostEnvironment,
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

        var customerReference = request.CustomerReference?.Trim().ToUpperInvariant();
        if (customerReference is not null && !IsValidCustomerReference(customerReference))
        {
            errors["customerReference"] = ["Customer reference must contain 1 to 64 letters, digits, hyphens, or underscores."];
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

        var actor = await currentCustomer.GetAsync(cancellationToken);
        var target = await customerProfiles.FindByReferenceAsync(
            customerReference!,
            cancellationToken);
        if (target is null)
        {
            await WriteAuthorizationAuditAsync(
                auditSink,
                AuthorizationAuditAction.CreateOrder,
                AuthorizationAuditResult.NotFound,
                actor.Id,
                null,
                null,
                AuthorizationCapability.OrdersWrite,
                httpContext,
                hostEnvironment,
                cancellationToken);
            return ResourceNotFound(httpContext);
        }

        var authorization = await authorizationService.AuthorizeAsync(
            httpContext.User,
            new CustomerResource(actor.Id, target.Id),
            new CustomerResourceRequirement());
        if (!authorization.Succeeded)
        {
            await WriteAuthorizationAuditAsync(
                auditSink,
                AuthorizationAuditAction.CreateOrder,
                AuthorizationAuditResult.Denied,
                actor.Id,
                target.Id,
                null,
                GetAuthorizationCapability(httpContext.User, actor.Id, target.Id, AuthorizationCapability.OrdersWrite),
                httpContext,
                hostEnvironment,
                cancellationToken);
            return ResourceNotFound(httpContext);
        }

        await WriteAuthorizationAuditAsync(
            auditSink,
            AuthorizationAuditAction.CreateOrder,
            AuthorizationAuditResult.Allowed,
            actor.Id,
            target.Id,
            null,
            GetAuthorizationCapability(httpContext.User, actor.Id, target.Id, AuthorizationCapability.OrdersWrite),
            httpContext,
            hostEnvironment,
            cancellationToken);

        var traceParent = Activity.Current?.Id;

        var result = await handler.Handle(
            new CreateOrderCommand(request.CustomerReference!, request.ProductSku!, request.Quantity),
            actor.Id,
            target.Id,
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
        HttpContext httpContext,
        GetOrderHandler handler,
        CurrentCustomerProfileAccessor currentCustomer,
        IAuthorizationService authorizationService,
        IAuthorizationAuditSink auditSink,
        IHostEnvironment hostEnvironment,
        CancellationToken cancellationToken) =>
    {
        var ownedOrder = await handler.Handle(orderId, cancellationToken);
        if (ownedOrder is null)
        {
            await WriteAuthorizationAuditAsync(
                auditSink,
                AuthorizationAuditAction.GetOrder,
                AuthorizationAuditResult.NotFound,
                null,
                null,
                orderId,
                AuthorizationCapability.OrdersRead,
                httpContext,
                hostEnvironment,
                cancellationToken);
            return ResourceNotFound(httpContext);
        }

        var actor = await currentCustomer.GetAsync(cancellationToken);
        var authorization = await authorizationService.AuthorizeAsync(
            httpContext.User,
            new CustomerResource(actor.Id, ownedOrder.Owner.CustomerProfileId),
            new CustomerResourceRequirement());
        var result = authorization.Succeeded ? AuthorizationAuditResult.Allowed : AuthorizationAuditResult.Denied;
        await WriteAuthorizationAuditAsync(
            auditSink,
            AuthorizationAuditAction.GetOrder,
            result,
            actor.Id,
            ownedOrder.Owner.CustomerProfileId,
            orderId,
            GetAuthorizationCapability(httpContext.User, actor.Id, ownedOrder.Owner.CustomerProfileId, AuthorizationCapability.OrdersRead),
            httpContext,
            hostEnvironment,
            cancellationToken);
        if (!authorization.Succeeded)
        {
            return ResourceNotFound(httpContext);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(ownedOrder.Response);
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

static IResult ResourceNotFound(HttpContext context) =>
    Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The requested resource was not found.",
        extensions: ProblemExtensions(context, "resource_not_found"));

static bool IsValidCustomerReference(string customerReference) =>
    customerReference.Length is >= 1 and <= 64 &&
    customerReference.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

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

static ValueTask WriteAuthorizationAuditAsync(
    IAuthorizationAuditSink auditSink,
    AuthorizationAuditAction action,
    AuthorizationAuditResult result,
    Guid? actorCustomerProfileId,
    Guid? targetCustomerProfileId,
    Guid? targetOrderId,
    AuthorizationCapability capability,
    HttpContext context,
    IHostEnvironment hostEnvironment,
    CancellationToken cancellationToken) =>
    auditSink.WriteAsync(
        new AuthorizationAuditEvent(
            action,
            result,
            actorCustomerProfileId,
            targetCustomerProfileId,
            targetOrderId,
            capability,
            Activity.Current?.Id ?? context.TraceIdentifier,
            hostEnvironment.EnvironmentName),
        cancellationToken);

static AuthorizationCapability GetAuthorizationCapability(
    System.Security.Claims.ClaimsPrincipal principal,
    Guid actorCustomerProfileId,
    Guid targetCustomerProfileId,
    AuthorizationCapability routeCapability) =>
    actorCustomerProfileId != targetCustomerProfileId
        && principal.FindAll("roles").Any(role => string.Equals(role.Value, CloudOrdersPermissions.AdminRole, StringComparison.Ordinal))
            ? AuthorizationCapability.UserAdmin
            : routeCapability;

static bool HasSingleExactClaim(System.Security.Claims.ClaimsPrincipal principal, string type, string expected) =>
    principal.FindAll(type).Select(claim => claim.Value).ToArray() is [var value]
    && string.Equals(value, expected, StringComparison.Ordinal);

static bool HasSingleAllowedClient(System.Security.Claims.ClaimsPrincipal principal, IEnumerable<string> allowedClientIds) =>
    principal.FindAll("azp").Select(claim => claim.Value).ToArray() is [var clientId]
    && Guid.TryParseExact(clientId, "D", out var parsedClientId)
    && allowedClientIds.Any(allowedClientId =>
        Guid.TryParseExact(allowedClientId, "D", out var parsedAllowedClientId)
        && parsedAllowedClientId == parsedClientId);

static bool HasDelegatedScope(System.Security.Claims.ClaimsPrincipal principal) =>
    principal.FindAll("scp").Select(claim => claim.Value).Any(value => !string.IsNullOrWhiteSpace(value));

static bool HasScope(System.Security.Claims.ClaimsPrincipal principal, string requiredScope) =>
    principal.FindAll("scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));

static bool HasOnlyKnownRoles(System.Security.Claims.ClaimsPrincipal principal) =>
    principal.FindAll("roles").All(role => string.Equals(role.Value, CloudOrdersPermissions.AdminRole, StringComparison.Ordinal));

public partial class Program;
