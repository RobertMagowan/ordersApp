using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CloudOrders.Contracts.Orders;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class OrderOwnershipIntegrationTests(SqlServerFixture sqlServer)
{
    private static readonly TestCustomer Alice = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "CUST-ALICE");
    private static readonly TestCustomer Bob = new(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("22222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        "CUST-BOB");

    [Fact]
    public async Task CustomerCannotCreateOrderForAnotherExistingCustomer()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var alice = factory.CreateAuthenticatedClient(Alice);
        using var bob = factory.CreateAuthenticatedClient(Bob);

        using var foreignResponse = await PostOrderAsync(alice, Bob.CustomerReference);
        using var absentResponse = await PostOrderAsync(alice, "CUST-ABSENT");

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absentResponse.StatusCode);
        await AssertEquivalentSafeNotFoundAsync(foreignResponse, absentResponse);
    }

    [Fact]
    public async Task InvalidCustomerReferenceIsRejectedBeforeProfileLookup()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var alice = factory.CreateAuthenticatedClient(Alice);

        using var response = await PostOrderAsync(alice, "CUST!");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task CustomerCannotReadAnotherCustomersOrderAndAdminCanReadIt()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var alice = factory.CreateAuthenticatedClient(Alice);
        using var bob = factory.CreateAuthenticatedClient(Bob);
        using var admin = factory.CreateAuthenticatedClient(Bob, ["user.admin"]);

        using var createResponse = await PostOrderAsync(alice, Alice.CustomerReference);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        using var foreignResponse = await bob.GetAsync($"/api/v1/orders/{created.Id}");
        using var absentResponse = await bob.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
        using var adminResponse = await admin.GetAsync($"/api/v1/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absentResponse.StatusCode);
        await AssertEquivalentSafeNotFoundAsync(foreignResponse, absentResponse);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Equal(created, await adminResponse.Content.ReadFromJsonAsync<OrderResponse>());
        Assert.Equal("no-store", adminResponse.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task SameIdempotencyKeyForDifferentActorsCreatesIndependentOrders()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var alice = factory.CreateAuthenticatedClient(Alice);
        using var bob = factory.CreateAuthenticatedClient(Bob);
        var idempotencyKey = Guid.NewGuid();

        using var aliceResponse = await PostOrderAsync(alice, Alice.CustomerReference, idempotencyKey);
        using var bobResponse = await PostOrderAsync(bob, Bob.CustomerReference, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, aliceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bobResponse.StatusCode);
        Assert.NotEqual(
            (await aliceResponse.Content.ReadFromJsonAsync<OrderResponse>())!.Id,
            (await bobResponse.Content.ReadFromJsonAsync<OrderResponse>())!.Id);
    }

    [Fact]
    public async Task SameActorAndKeyForDifferentTargetsConflicts()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var admin = factory.CreateAuthenticatedClient(Alice, ["user.admin"]);
        using var bob = factory.CreateAuthenticatedClient(Bob);
        var idempotencyKey = Guid.NewGuid();

        using var ownResponse = await PostOrderAsync(admin, Alice.CustomerReference, idempotencyKey);
        using var crossCustomerResponse = await PostOrderAsync(admin, Bob.CustomerReference, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, crossCustomerResponse.StatusCode);
    }

    [Fact]
    public async Task RemovedAdminCannotReplayAnotherCustomersOrder()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = new OrderSqlJwtBearerWebApplicationFactory(database.ConnectionString);
        using var admin = factory.CreateAuthenticatedClient(Alice, ["user.admin"]);
        using var bob = factory.CreateAuthenticatedClient(Bob);
        var idempotencyKey = Guid.NewGuid();

        using var createResponse = await PostOrderAsync(admin, Bob.CustomerReference, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var revokedAdmin = factory.CreateAuthenticatedClient(Alice);
        using var replayResponse = await PostOrderAsync(revokedAdmin, Bob.CustomerReference, idempotencyKey);

        Assert.Equal(HttpStatusCode.NotFound, replayResponse.StatusCode);
    }

    private static Task<HttpResponseMessage> PostOrderAsync(
        HttpClient client,
        string customerReference,
        Guid? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(customerReference, "SKU-OWNERSHIP", 1))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", (idempotencyKey ?? Guid.NewGuid()).ToString("D"));
        return client.SendAsync(request);
    }

    private static async Task AssertEquivalentSafeNotFoundAsync(
        HttpResponseMessage first,
        HttpResponseMessage second)
    {
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        foreach (var property in new[] { "type", "title", "status", "errorCode" })
        {
            Assert.Equal(
                firstBody.RootElement.GetProperty(property).GetRawText(),
                secondBody.RootElement.GetProperty(property).GetRawText());
        }

        Assert.Equal("resource_not_found", firstBody.RootElement.GetProperty("errorCode").GetString());
        Assert.NotEqual(
            firstBody.RootElement.GetProperty("traceId").GetString(),
            secondBody.RootElement.GetProperty("traceId").GetString());
    }

}
