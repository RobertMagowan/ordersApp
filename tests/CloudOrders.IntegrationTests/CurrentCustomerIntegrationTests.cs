using System.Net;
using System.Text.Json;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class CurrentCustomerIntegrationTests(SqlServerFixture sqlServer)
{
    private static readonly TestCustomer Alice = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "CUST-ALICE");

    [Fact]
    public async Task GetCurrentCustomerReturnsOnlyTheAuthenticatedProfilesStableReference()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateAuthenticatedClient(Alice);

        using var firstResponse = await client.GetAsync("/api/v1/me");
        using var secondResponse = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var firstBody = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondBody = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(Alice.CustomerReference, firstBody.RootElement.GetProperty("customerReference").GetString());
        Assert.Equal(firstBody.RootElement.GetRawText(), secondBody.RootElement.GetRawText());
        Assert.Single(firstBody.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task GetCurrentCustomerRejectsUnauthenticatedRequests()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentCustomerRequiresOrdersReadScope()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateAuthenticatedClient(Alice, scope: "Orders.Write");

        using var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
