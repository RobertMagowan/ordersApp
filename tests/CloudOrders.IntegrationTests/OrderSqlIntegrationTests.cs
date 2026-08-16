using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CloudOrders.Contracts.Orders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class OrderSqlIntegrationTests(SqlServerFixture sqlServer)
{
    private const string SubjectId = "local-development-subject";

    [Fact]
    public async Task ReadinessChecksSqlConnection()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessRejectsAnUnmigratedSqlDatabase()
    {
        await using var database = await sqlServer.CreateEmptyDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task MigrationFirstCreateAndGetPersistAnOrder()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await PostOrderAsync(client, Guid.NewGuid(), "CUST-001", "SKU-001", 2);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(created);
        Assert.Equal("pending", created.Status);
        Assert.Equal($"/api/v1/orders/{created.Id}", response.Headers.Location?.OriginalString);

        using var getResponse = await client.GetAsync($"/api/v1/orders/{created.Id}");
        var read = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created, read);
        Assert.Equal(1, await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.Orders"));
        Assert.Equal(1, await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory"));
    }

    [Fact]
    public async Task FirstCreatePersistsOrderOutboxAndIdempotencyInSql()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        using var request = CreateOrderRequestMessage(key, " CUST-002 ", " sku.002 ", 3);
        request.Headers.TryAddWithoutValidation("traceparent", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(created);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "Orders"));
        Assert.Equal(1, await CountAsync(connection, "OutboxMessages"));
        Assert.Equal(1, await CountAsync(connection, "IdempotencyRecords"));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.OrderId, o.AggregateId, o.MessageType, o.MessageVersion, o.Payload, o.AttemptCount, o.TraceParent,
                   i.SubjectId, i.IdempotencyKey, i.RequestHash, i.OrderId, i.ResponseStatus, i.ResponseJson
            FROM dbo.OutboxMessages AS o
            CROSS JOIN dbo.IdempotencyRecords AS i;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(created.Id, reader.GetGuid(0));
        Assert.Equal(created.Id, reader.GetGuid(1));
        Assert.Equal(OrderCreatedIntegrationEventV1.MessageType, reader.GetString(2));
        Assert.Equal(OrderCreatedIntegrationEventV1.CurrentMessageVersion, reader.GetInt32(3));
        using var payload = JsonDocument.Parse(reader.GetString(4));
        Assert.Equal(created.Id, payload.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(0, reader.GetInt32(5));
        Assert.StartsWith("00-4bf92f3577b34da6a3ce929d0e0e4736-", reader.GetString(6), StringComparison.Ordinal);
        Assert.Equal(SubjectId, reader.GetString(7));
        Assert.Equal(key, reader.GetGuid(8));
        Assert.Equal(
            "997f348773208c1776ccab100c067a8633dea47507ffb8e945b8bdd08f2eb312",
            Convert.ToHexString((byte[])reader[9]).ToLowerInvariant());
        Assert.Equal(created.Id, reader.GetGuid(10));
        Assert.Equal(StatusCodes.Status201Created, reader.GetInt32(11));
        Assert.Equal(created, JsonSerializer.Deserialize<OrderResponse>(reader.GetString(12), JsonOptions));
    }

    [Fact]
    public async Task ExactReplayReturnsOriginalRepresentationAndReplayHeader()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();

        using var firstResponse = await PostOrderAsync(client, key, "CUST-003", "SKU-003", 4);
        using var replayResponse = await PostOrderAsync(client, key, "CUST-003", "SKU-003", 4);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal("true", Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(
            await firstResponse.Content.ReadAsStringAsync(),
            await replayResponse.Content.ReadAsStringAsync());
        await AssertSingleTransactionRowsAsync(database.ConnectionString);
    }

    [Fact]
    public async Task CanonicalEquivalentReplayReturnsOriginalRepresentation()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();

        using var firstResponse = await PostOrderAsync(client, key, " cust-004 ", "sku.004", 5);
        using var replayResponse = await PostOrderAsync(client, key, "CUST-004", " SKU.004 ", 5);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(
            await firstResponse.Content.ReadAsStringAsync(),
            await replayResponse.Content.ReadAsStringAsync());
        await AssertSingleTransactionRowsAsync(database.ConnectionString);
    }

    [Fact]
    public async Task SameKeyWithDifferentCanonicalPayloadReturnsConflictProblem()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();

        using var firstResponse = await PostOrderAsync(client, key, "CUST-005", "SKU-005", 1);
        using var conflictResponse = await PostOrderAsync(client, key, "CUST-005", "SKU-005", 2);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        await AssertProblemDetailsAsync(conflictResponse, HttpStatusCode.Conflict, "idempotency_conflict");
        await AssertSingleTransactionRowsAsync(database.ConnectionString);
    }

    [Fact]
    public async Task ConcurrentSameKeySubmissionsCreateOneTransactionSlice()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(
            PostOrderAsync(firstClient, key, "CUST-006", "SKU-006", 6),
            PostOrderAsync(secondClient, key, "CUST-006", "SKU-006", 6));

        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Created],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var representations = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Equal(representations[0], representations[1]);
        await AssertSingleTransactionRowsAsync(database.ConnectionString);
    }

    [Fact]
    public async Task ConcurrentSameKeyDifferentPayloadSubmissionsCreateOneOrderAndReturnConflict()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(
            PostOrderAsync(firstClient, key, "CUST-006", "SKU-006", 6),
            PostOrderAsync(secondClient, key, "CUST-006", "SKU-006", 7));

        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertProblemDetailsAsync(conflict, HttpStatusCode.Conflict, "idempotency_conflict");
        await AssertSingleTransactionRowsAsync(database.ConnectionString);
    }

    [Fact]
    public async Task ExpiredIdempotencyRecordAllowsANewOrderWithTheSameKey()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();

        using var firstResponse = await PostOrderAsync(client, key, "CUST-009", "SKU-009", 1);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        await ExecuteAsync(
            database.ConnectionString,
            "UPDATE dbo.IdempotencyRecords SET ExpiresAt = DATEADD(day, -1, SYSUTCDATETIME()) WHERE IdempotencyKey = @key",
            command => command.Parameters.AddWithValue("@key", key));

        using var secondResponse = await PostOrderAsync(client, key, "CUST-009", "SKU-009", 2);

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(2, await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.Orders"));
        Assert.Equal(2, await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.OutboxMessages"));
        Assert.Equal(1, await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.IdempotencyRecords"));
    }

    [Fact]
    public async Task OversizedTraceParentDoesNotPreventOrderCreation()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var request = CreateOrderRequestMessage(Guid.NewGuid(), "CUST-010", "SKU-010", 1);
        request.Headers.TryAddWithoutValidation("traceparent", new string('a', 513));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(await ScalarAsync<int>(database.ConnectionString, "SELECT LEN(TraceParent) FROM dbo.OutboxMessages") <= 512);
    }

    [Fact]
    public async Task OrderAndIdempotencyReplaySurviveApiRestart()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var key = Guid.NewGuid();
        OrderResponse created;

        using (var firstFactory = CreateFactory(database.ConnectionString))
        using (var firstClient = firstFactory.CreateClient())
        using (var firstResponse = await PostOrderAsync(firstClient, key, "CUST-007", "SKU-007", 7))
        {
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
            created = (await firstResponse.Content.ReadFromJsonAsync<OrderResponse>())!;
        }

        using var restartedFactory = CreateFactory(database.ConnectionString);
        using var restartedClient = restartedFactory.CreateClient();
        using var getResponse = await restartedClient.GetAsync($"/api/v1/orders/{created.Id}");
        using var replayResponse = await PostOrderAsync(restartedClient, key, "CUST-007", "SKU-007", 7);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created, await getResponse.Content.ReadFromJsonAsync<OrderResponse>());
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(created, await replayResponse.Content.ReadFromJsonAsync<OrderResponse>());
        await AssertSingleTransactionRowsAsync(database.ConnectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    public async Task MissingOrInvalidIdempotencyKeyReturnsProblemDetails(string? idempotencyKey)
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest("CUST-008", "SKU-008", 1))
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        using var response = await client.SendAsync(request);

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "invalid_idempotency_key");
        Assert.Equal(0, await ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM dbo.Orders"));
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CloudOrders"] = connectionString
                }));
        });

    private static async Task<HttpResponseMessage> PostOrderAsync(
        HttpClient client,
        Guid idempotencyKey,
        string customerReference,
        string productSku,
        int quantity)
    {
        using var request = CreateOrderRequestMessage(idempotencyKey, customerReference, productSku, quantity);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateOrderRequestMessage(
        Guid idempotencyKey,
        string customerReference,
        string productSku,
        int quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(customerReference, productSku, quantity))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString("D", CultureInfo.InvariantCulture));
        return request;
    }

    private static async Task AssertSingleTransactionRowsAsync(string connectionString)
    {
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.Orders"));
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.OutboxMessages"));
        Assert.Equal(1, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.IdempotencyRecords"));
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedErrorCode, document.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
    }

    private static async Task<int> CountAsync(SqlConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.{tableName}";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        Action<SqlCommand> configure)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        await command.ExecuteNonQueryAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
