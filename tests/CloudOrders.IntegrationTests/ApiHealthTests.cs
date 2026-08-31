using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CloudOrders.IntegrationTests;

public sealed class ApiTests : IDisposable
{
    private readonly SignedJwtFactory tokens = new();
    private readonly JwtBearerWebApplicationFactory factory;

    public ApiTests() => factory = new JwtBearerWebApplicationFactory(tokens);
    [Fact]
    public async Task LiveHealthEndpointReturnsOkWithoutDatabase()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void ProductionStartupFailsClearlyWithoutSqlConnection()
    {
        using var productionFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => productionFactory.CreateClient());

        Assert.Contains("ConnectionStrings:CloudOrders", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOrderRejectsUnknownJsonMembers()
    {
        using var client = CreateAuthenticatedClient();
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
        using var client = CreateAuthenticatedClient();
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
        using var client = CreateAuthenticatedClient();
        using var content = new StringContent(payload, Encoding.UTF8, mediaType);

        using var response = await client.PostAsync("/api/v1/orders", content);

        await AssertProblemDetails(response, expectedStatus, "invalid_request");
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

    public void Dispose()
    {
        factory.Dispose();
        tokens.Dispose();
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.CreateToken(oid: Guid.NewGuid().ToString("D"), scope: "Orders.Read Orders.Write"));
        return client;
    }
}
