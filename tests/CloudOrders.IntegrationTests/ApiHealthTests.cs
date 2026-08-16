using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

    [Theory]
    [InlineData("{\"productSku\":\"SKU-001\",\"quantity\":1}")]
    [InlineData("{\"customerReference\":null,\"productSku\":\"SKU-001\",\"quantity\":1}")]
    [InlineData("{\"customerReference\":\"CUST-001\",\"quantity\":1}")]
    [InlineData("{\"customerReference\":\"CUST-001\",\"productSku\":null,\"quantity\":1}")]
    public async Task CreateOrderReturnsValidationProblemForMissingOrNullRequiredStrings(string payload)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/orders", content);

        await AssertProblemDetails(response, HttpStatusCode.BadRequest, "validation_error");
    }

    [Theory]
    [InlineData("{\"customerReference\":\"CUST-001\",\"productSku\":\"SKU-001\",\"quantity\":1,\"unexpected\":true}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"customerReference\":", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("order", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task CreateOrderReturnsProblemDetailsForBindingFailures(
        string payload,
        string mediaType,
        HttpStatusCode expectedStatus)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(payload, Encoding.UTF8, mediaType);

        using var response = await client.PostAsync("/api/v1/orders", content);

        await AssertProblemDetails(response, expectedStatus, "invalid_request");
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

    private static async Task AssertProblemDetails(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedErrorCode, root.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }
}
