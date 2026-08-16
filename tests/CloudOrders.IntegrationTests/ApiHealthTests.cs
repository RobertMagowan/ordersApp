using System.Net;
using System.Net.Http.Json;
using CloudOrders.Contracts.Orders;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CloudOrders.IntegrationTests;

public sealed class ApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task LiveHealthEndpointReturnsOkWithoutDatabase()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrderRejectsUnknownJsonMembers()
    {
        using var client = factory.CreateClient();
        using var content = JsonContent.Create(new
        {
            customerReference = "CUST-001",
            productSku = "SKU-001",
            quantity = 1,
            unexpected = true
        });

        using var response = await client.PostAsync("/api/v1/orders", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrderReturnsPendingResourceThatCanBeRead()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/orders",
            new CreateOrderRequest("CUST-002", "SKU-002", 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(created);
        Assert.Equal("pending", created.Status);
        Assert.Equal($"/api/v1/orders/{created.Id}", response.Headers.Location?.OriginalString);

        using var readResponse = await client.GetAsync($"/api/v1/orders/{created.Id}");
        var read = await readResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(created.Id, read?.Id);
        Assert.Equal("CUST-002", read?.CustomerReference);
    }
}
