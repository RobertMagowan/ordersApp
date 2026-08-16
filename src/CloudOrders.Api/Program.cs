using System.Text.Json;
using System.Text.Json.Serialization;
using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using CloudOrders.Infrastructure.InMemory;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
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

app.MapGet("/health/live", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("LiveHealth")
    .WithTags("Health");

app.MapGet("/health/ready", () => TypedResults.Ok(new { status = "ready" }))
    .WithName("ReadyHealth")
    .WithTags("Health");

app.MapPost("/api/v1/orders", async (
        CreateOrderRequest request,
        CreateOrderHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.Handle(
            new CreateOrderCommand(request.CustomerReference, request.ProductSku, request.Quantity),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["request"] = [result.ErrorMessage ?? "The request is invalid."]
                },
                statusCode: StatusCodes.Status400BadRequest,
                title: "The order request is invalid.");
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

public partial class Program;
