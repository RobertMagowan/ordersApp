using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CloudOrders.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class MigrationRunnerTests(SqlServerFixture sqlServerFixture)
{
    [Fact]
    public async Task MigrationRunnerAppliesCommittedMigrations()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var result = await RunRunnerAsync(database.ConnectionString);

        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using var context = new CloudOrdersDbContext(options);

        Assert.True(result.ExitCode == 0, result.StandardError);
        Assert.Contains(
            "20260816221235_InitialSqlPersistence",
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
        Assert.Contains(
            "20260829075044_AddCustomerProfileOwnershipExpand",
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None));
        Assert.Contains(
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None),
            migration => migration.EndsWith("_EnforceCustomerProfileOwnership", StringComparison.Ordinal));
    }

    [Fact]
    public async Task E2MakesOwnershipRequiredAndUsesActorKeyIdentityWhileRetainingSubjectId()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var result = await RunRunnerAsync(database.ConnectionString);

        Assert.Equal(0, result.ExitCode);
        await using var context = new CloudOrdersDbContext(
            new DbContextOptionsBuilder<CloudOrdersDbContext>().UseSqlServer(database.ConnectionString).Options);

        var nullability = await context.Database.SqlQuery<ColumnShape>($"""
            SELECT TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND ((TABLE_NAME = 'Orders' AND COLUMN_NAME = 'CustomerProfileId')
                OR (TABLE_NAME = 'IdempotencyRecords' AND COLUMN_NAME IN ('ActorCustomerProfileId', 'TargetCustomerProfileId', 'SubjectId')))
            """).ToListAsync();

        Assert.Equal("NO", nullability.Single(x => x.TableName == "Orders").IsNullable);
        Assert.Equal("NO", nullability.Single(x => x.ColumnName == "ActorCustomerProfileId").IsNullable);
        Assert.Equal("NO", nullability.Single(x => x.ColumnName == "TargetCustomerProfileId").IsNullable);
        Assert.Equal("YES", nullability.Single(x => x.ColumnName == "SubjectId").IsNullable);

        var keyColumns = await context.Database.SqlQuery<KeyColumn>($"""
            SELECT c.name AS ColumnName
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.IdempotencyRecords') AND i.is_primary_key = 1
            ORDER BY ic.key_ordinal
            """).ToListAsync();

        Assert.Equal(["ActorCustomerProfileId", "IdempotencyKey"], keyColumns.Select(x => x.ColumnName));
    }

    [Fact]
    public async Task MigrationRunnerFailsWhenConnectionStringIsMissing()
    {
        var result = await RunRunnerAsync(null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SQL migration requires configuration key ConnectionStrings:CloudOrders.", result.StandardError);
    }

    [Fact]
    public async Task OwnershipPreconditionRunnerSucceedsForCompliantSchema()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var initialResult = await RunRunnerAsync(database.ConnectionString);

        var result = await RunRunnerAsync(database.ConnectionString, ownershipPrecondition: true);

        Assert.Equal(0, initialResult.ExitCode);
        Assert.True(result.ExitCode == 0, result.StandardError);
        Assert.Contains("SQL ownership precondition passed.", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrationRunnerFailsWhenMigrationCannotConnect()
    {
        const string invalidConnectionString = "Server=localhost,1;Initial Catalog=CloudOrders;User ID=invalid;Password=invalid;Connect Timeout=1;Encrypt=False";
        var result = await RunRunnerAsync(invalidConnectionString);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SQL migration failed:", result.StandardError);
        Assert.Contains("detail=SqlErrorNumber=", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("; SqlErrorState=", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("; SqlErrorClass=", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidConnectionString, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyReleaseAppliesOnlyTheAuthorisedOutstandingSuffix()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, "20260907142134_EnforceCustomerProfileOwnership");
        var descriptorPath = CreateDescriptorFile(FullBaseline, FullBaseline);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--apply-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.Equal(0, result.ExitCode);
            using var evidence = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("integration-test-release", evidence.RootElement.GetProperty("releaseId").GetString());
            Assert.Matches("^[a-f0-9]{64}$", evidence.RootElement.GetProperty("descriptorSha256").GetString());
            Assert.Equal("apply", evidence.RootElement.GetProperty("mode").GetString());
            Assert.Contains("20260909213051_AddOutboxLeasing", evidence.RootElement.GetProperty("applied").ToString(), StringComparison.Ordinal);
            Assert.Empty(evidence.RootElement.GetProperty("outstanding").EnumerateArray());
            Assert.DoesNotContain(database.ConnectionString, result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task ReleaseVerificationPersistsAndVerifiesSchemaOnTheSameDatabase()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, FullBaseline[0]);
        var descriptorPath = Path.Combine(RepositoryRoot(), "ops", "releases", "current-release.json");

        var before = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");
        Assert.Equal(0, before.ExitCode);
        using (var beforeEvidence = JsonDocument.Parse(before.StandardOutput))
        {
            Assert.Equal("verify", beforeEvidence.RootElement.GetProperty("mode").GetString());
            Assert.Equal(3, beforeEvidence.RootElement.GetProperty("outstanding").GetArrayLength());
        }

        var apply = await RunRunnerAsync(
            database.ConnectionString,
            ["--apply-release", descriptorPath],
            deploymentEnvironment: "development");
        Assert.True(apply.ExitCode == 0, apply.StandardError);
        Assert.DoesNotContain(database.ConnectionString, apply.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(database.ConnectionString, apply.StandardError, StringComparison.Ordinal);

        var after = await RunRunnerAsync(
            database.ConnectionString,
            ["--verify-release", descriptorPath],
            deploymentEnvironment: "development");
        Assert.True(after.ExitCode == 0, after.StandardError);
        using (var afterEvidence = JsonDocument.Parse(after.StandardOutput))
        {
            Assert.Equal("verify", afterEvidence.RootElement.GetProperty("mode").GetString());
            Assert.Empty(afterEvidence.RootElement.GetProperty("outstanding").EnumerateArray());
        }

        Assert.Equal(FullBaseline, await GetAppliedMigrationsAsync(database.ConnectionString));

        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                SELECT c.name AS ColumnName
                FROM sys.columns c
                WHERE c.object_id = OBJECT_ID(N'dbo.OutboxMessages')
                  AND c.name IN (N'LeaseExpiresAt', N'LeaseOwner', N'LeaseToken')
                ORDER BY c.name;
                SELECT i.name
                FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID(N'dbo.OutboxMessages')
                  AND i.name = N'IX_OutboxMessages_Lease';
                """;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var leaseColumns = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            leaseColumns.Add(reader.GetString(0));
        }

        Assert.Equal(["LeaseExpiresAt", "LeaseOwner", "LeaseToken"], leaseColumns);
        Assert.True(await reader.NextResultAsync(CancellationToken.None));
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("IX_OutboxMessages_Lease", reader.GetString(0));
    }

    [Fact]
    public async Task ApplyReleaseWithAnEqualBaselineIsAVerifiedNoOp()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, FullBaseline[^1]);
        var descriptorPath = CreateDescriptorFile(FullBaseline, []);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--apply-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.Equal(0, result.ExitCode);
            using var evidence = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal("apply", evidence.RootElement.GetProperty("mode").GetString());
            Assert.Empty(evidence.RootElement.GetProperty("outstanding").EnumerateArray());
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyCodeOnlyReleaseRetainsCumulativeAuthorisationOutsideTheDescriptorBaseline()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, "20260907142134_EnforceCustomerProfileOwnership");
        var codeOnlyBaseline = FullBaseline[..^1];
        var descriptorPath = CreateDescriptorFile(codeOnlyBaseline, codeOnlyBaseline);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.Equal(0, result.ExitCode);
            using var evidence = JsonDocument.Parse(result.StandardOutput);
            Assert.Empty(evidence.RootElement.GetProperty("outstanding").EnumerateArray());
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyReleaseRejectsAnUnknownAppliedMigrationIdWithoutLeakingTheConnectionString()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, FullBaseline[^1]);
        await InsertHistoryAsync(database.ConnectionString, "99999999999999_UnknownMigration");
        var descriptorPath = CreateDescriptorFile(FullBaseline, []);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=MIGRATION_BASELINE_CONFLICT", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyReleaseRejectsAMigrationHistoryHole()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, FullBaseline[^1]);
        await DeleteHistoryAsync(database.ConnectionString, "20260829075044_AddCustomerProfileOwnershipExpand");
        var descriptorPath = CreateDescriptorFile(FullBaseline, []);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=MIGRATION_BASELINE_CONFLICT", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyReleaseRejectsADatabaseAheadOfTheDescriptorBaseline()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, FullBaseline[^1]);
        var descriptorPath = CreateDescriptorFile(FullBaseline[..^1], []);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=MIGRATION_BASELINE_CONFLICT", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyReleaseRejectsAnUnauthorisedOutstandingMigration()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, "20260907142134_EnforceCustomerProfileOwnership");
        var descriptorPath = CreateDescriptorFile(FullBaseline, []);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=MIGRATION_STATE_CONFLICT", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyReleaseRejectsAuthorisationThatIsNotAnOrderedBaselinePrefix()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, "20260907142134_EnforceCustomerProfileOwnership");
        var descriptorPath = CreateDescriptorFile(FullBaseline, ["20260909213051_AddOutboxLeasing"]);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=RELEASE_DESCRIPTOR_INVALID", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task ApplyReleaseAppliesAnOrderedMultiMigrationSuffix()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, FullBaseline[0]);
        var descriptorPath = CreateDescriptorFile(FullBaseline, FullBaseline);

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--apply-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(FullBaseline, await GetAppliedMigrationsAsync(database.ConnectionString));
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task ConcurrentReleaseApplicationsWaitForTheDatabaseGuardAndPreserveMigrationState()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        await ApplyThroughAsync(database.ConnectionString, "20260829075044_AddCustomerProfileOwnershipExpand");
        await InsertConcurrentMigrationMarkerAsync(database.ConnectionString);
        var descriptorPath = CreateDescriptorFile(FullBaseline, FullBaseline);
        await using var guardConnection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString);
        await guardConnection.OpenAsync(CancellationToken.None);
        await AcquireReleaseGuardAsync(guardConnection);
        var guardHeld = true;

        try
        {
            var firstApply = RunRunnerAsync(database.ConnectionString, ["--apply-release", descriptorPath], deploymentEnvironment: "development");
            var secondApply = RunRunnerAsync(database.ConnectionString, ["--apply-release", descriptorPath], deploymentEnvironment: "development");

            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.False(firstApply.IsCompleted);
            Assert.False(secondApply.IsCompleted);

            await ReleaseReleaseGuardAsync(guardConnection);
            guardHeld = false;
            var results = await Task.WhenAll(firstApply, secondApply);

            Assert.All(results, result => Assert.Equal(0, result.ExitCode));
            Assert.Equal(FullBaseline, await GetAppliedMigrationsAsync(database.ConnectionString));
            Assert.Equal(1, await CountConcurrentMigrationMarkersAsync(database.ConnectionString));
        }
        finally
        {
            if (guardHeld)
            {
                await ReleaseReleaseGuardAsync(guardConnection);
            }
            File.Delete(descriptorPath);
        }
    }

    [Fact]
    public async Task VerifyReleaseRejectsAnInvalidDescriptorWithoutLeakingTheConnectionString()
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var descriptorPath = CreateDescriptorFile(FullBaseline, FullBaseline, precondition: "unexpected-policy");

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=RELEASE_DESCRIPTOR_INVALID", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Theory]
    [InlineData("missing-deploy-api")]
    [InlineData("unknown-property")]
    [InlineData("null-array")]
    [InlineData("invalid-array")]
    public async Task VerifyReleaseRejectsDescriptorsWithAnInvalidJsonShape(string invalidShape)
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();
        var descriptorPath = CreateDescriptorFile(FullBaseline, FullBaseline, mutate: descriptor =>
        {
            switch (invalidShape)
            {
                case "missing-deploy-api":
                    descriptor.Remove("deployApi");
                    break;
                case "unknown-property":
                    descriptor["unrecognised"] = true;
                    break;
                case "null-array":
                    descriptor["authorisedMigrations"] = null;
                    break;
                case "invalid-array":
                    descriptor["environments"] = new JsonArray("development", 1);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported test shape {invalidShape}.");
            }
        });

        try
        {
            var result = await RunRunnerAsync(
                database.ConnectionString,
                ["--verify-release", descriptorPath],
                deploymentEnvironment: "development");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("category=RELEASE_DESCRIPTOR_INVALID", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(descriptorPath);
        }
    }

    [Theory]
    [InlineData("--verify-release")]
    [InlineData("--apply-release")]
    [InlineData("--verify-release", "descriptor.json", "unexpected")]
    [InlineData("--migration", "AddOutboxLeasing")]
    public async Task MigrationRunnerRejectsInvalidArgumentsWithoutLeakingTheConnectionString(params string[] arguments)
    {
        await using var database = await sqlServerFixture.CreateEmptyDatabaseAsync();

        var result = await RunRunnerAsync(database.ConnectionString, arguments, deploymentEnvironment: "development");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(database.ConnectionString, result.StandardError, StringComparison.Ordinal);
    }

    private static async Task ApplyThroughAsync(string connectionString, string targetMigration)
    {
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new CloudOrdersDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static async Task InsertHistoryAsync(string connectionString, string migrationId)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (@migrationId, @productVersion)";
        command.Parameters.AddWithValue("@migrationId", migrationId);
        command.Parameters.AddWithValue("@productVersion", "10.0.0");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task DeleteHistoryAsync(string connectionString, string migrationId)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = @migrationId";
        command.Parameters.AddWithValue("@migrationId", migrationId);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<string[]> GetAppliedMigrationsAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new CloudOrdersDbContext(options);
        return (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();
    }

    private static async Task AcquireReleaseGuardAsync(Microsoft.Data.SqlClient.SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = N'CloudOrders.ReleaseMigrationRunner', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0;
            IF @result < 0 THROW 51002, 'Could not acquire release migration guard.', 1;
            """;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task ReleaseReleaseGuardAsync(Microsoft.Data.SqlClient.SqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_releaseapplock @Resource = N'CloudOrders.ReleaseMigrationRunner', @LockOwner = N'Session';";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task InsertConcurrentMigrationMarkerAsync(string connectionString)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @profileId uniqueidentifier = '11111111-1111-1111-1111-111111111111';
            DECLARE @orderId uniqueidentifier = '22222222-2222-2222-2222-222222222222';
            INSERT INTO dbo.CustomerProfiles (Id, CustomerReference, Issuer, ObjectId, ContactEmail, CreatedAt, UpdatedAt)
            VALUES (@profileId, 'concurrency-profile', 'https://issuer.example', '33333333-3333-3333-3333-333333333333', 'marker@example.test', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            INSERT INTO dbo.Orders (Id, CustomerReference, ProductSku, Quantity, Status, CreatedAt, UpdatedAt, CustomerProfileId)
            VALUES (@orderId, 'concurrency-order', 'marker-sku', 1, 'Pending', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), @profileId);
            INSERT INTO dbo.OutboxMessages (EventId, OrderId, AggregateId, MessageType, MessageVersion, Payload, OccurredAt, CreatedAt, ProcessedAt, AttemptCount, LastAttemptAt, LastErrorCode, TraceParent)
            VALUES ('44444444-4444-4444-4444-444444444444', @orderId, @orderId, 'concurrency-marker', 1, '{}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NULL, 0, NULL, NULL, NULL);
            """;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> CountConcurrentMigrationMarkersAsync(string connectionString)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.OutboxMessages WHERE MessageType = 'concurrency-marker';";
        return Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateDescriptorFile(
        string[] baseline,
        string[] authorisedMigrations,
        string precondition = "none",
        Action<JsonObject>? mutate = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cloudorders-release-{Guid.NewGuid():N}.json");
        var descriptor = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            releaseId = "integration-test-release",
            schemaVersion = 1,
            environments = DevelopmentEnvironment,
            deployApi = true,
            requiredMigrationBaseline = baseline,
            authorisedMigrations,
            precondition,
            trafficPolicy = "none",
            compatibility = "api-compatible"
        }))!.AsObject();
        mutate?.Invoke(descriptor);
        File.WriteAllText(path, descriptor.ToJsonString());
        return path;
    }

    private static async Task<MigrationRunResult> RunRunnerAsync(
        string? connectionString,
        string? migration = null,
        bool ownershipPrecondition = false) =>
        await RunRunnerAsync(
            connectionString,
            ownershipPrecondition ? ["--ownership-precondition"] : migration is null ? [] : ["--migration", migration]);

    private static async Task<MigrationRunResult> RunRunnerAsync(
        string? connectionString,
        string[] arguments,
        string? deploymentEnvironment = null)
    {
        var runnerAssembly = Path.Combine(
            RepositoryRoot(),
            "src",
            "CloudOrders.Migrations",
            "bin",
            "Release",
            "net10.0",
            "CloudOrders.Migrations.dll");
        Assert.True(File.Exists(runnerAssembly), $"Expected built migration runner at {runnerAssembly}.");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(runnerAssembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment.Remove("ConnectionStrings__CloudOrders");
        startInfo.Environment.Remove("ConnectionStrings:CloudOrders");
        startInfo.Environment.Remove("DEPLOYMENT_ENVIRONMENT");

        if (connectionString is not null)
        {
            startInfo.Environment["ConnectionStrings__CloudOrders"] = connectionString;
        }

        if (deploymentEnvironment is not null)
        {
            startInfo.Environment["DEPLOYMENT_ENVIRONMENT"] = deploymentEnvironment;
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);

        return new MigrationRunResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudOrders.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate the repository root.");
    }

    private sealed record MigrationRunResult(int ExitCode, string StandardOutput, string StandardError);

    private static readonly string[] FullBaseline =
    [
        "20260816221235_InitialSqlPersistence",
        "20260829075044_AddCustomerProfileOwnershipExpand",
        "20260907142134_EnforceCustomerProfileOwnership",
        "20260909213051_AddOutboxLeasing"
    ];

    private static readonly string[] DevelopmentEnvironment = ["development"];

    private sealed record ColumnShape(string TableName, string ColumnName, string IsNullable);

    private sealed record KeyColumn(string ColumnName);
}
