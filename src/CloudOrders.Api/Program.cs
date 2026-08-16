using System.Text.Json;
using System.Text.Json.Serialization;
using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using CloudOrders.Infrastructure.InMemory;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IOutboxWriter, InMemoryOutboxWriter>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
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

app.MapGet("/health/ready", () => TypedResults.Ok(new { status = "ready" }))
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

        var result = await handler.Handle(
            new CreateOrderCommand(request.CustomerReference!, request.ProductSku!, request.Quantity),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return OrderValidationProblem(
                httpContext,
                new Dictionary<string, string[]>
                {
                    ["request"] = [result.ErrorMessage ?? "The request is invalid."]
                });
        }

        return Results.Created($"/api/v1/orders/{result.Value!.Id}", result.Value);
    })
    .WithName("CreateOrder")
    .WithTags("Orders");

app.MapGet("/api/v1/orders/{orderId:guid}", async (
        Guid orderId,
        GetOrderHandler handler,
        CancellationToken cancellationToken) =>
    {
        var response = await handler.Handle(orderId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    })
    .WithName("GetOrder")
    .WithTags("Orders");

app.Run();

static IResult OrderValidationProblem(HttpContext context, IDictionary<string, string[]> errors) =>
    Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The order request is invalid.",
        extensions: new Dictionary<string, object?>(ProblemExtensions(context, "validation_error"))
        {
            ["errors"] = errors
        });

static IDictionary<string, object?> ProblemExtensions(HttpContext context, string errorCode) =>
    new Dictionary<string, object?>
    {
        ["errorCode"] = errorCode,
        ["traceId"] = context.TraceIdentifier
    };

public partial class Program;
