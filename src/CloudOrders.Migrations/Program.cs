using System.Security.Cryptography;
using System.Text.Json;
using CloudOrders.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

RunnerArguments runnerArguments;
try
{
    runnerArguments = ParseArguments(args);
}
catch (ArgumentException)
{
    Console.Error.WriteLine("SQL migration accepts no arguments, '--ownership-precondition', '--verify-release <descriptor>', or '--apply-release <descriptor>'.");
    return 1;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CloudOrders")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings:CloudOrders");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("SQL migration requires configuration key ConnectionStrings:CloudOrders.");
    return 1;
}

try
{
    var options = new DbContextOptionsBuilder<CloudOrdersDbContext>()
        .UseSqlServer(connectionString)
        .Options;
    await using var context = new CloudOrdersDbContext(options);

    switch (runnerArguments.Mode)
    {
        case RunnerMode.OwnershipPrecondition:
            await RunOwnershipPreconditionAsync(context);
            Console.WriteLine("SQL ownership precondition passed.");
            break;
        case RunnerMode.LocalDevelopment:
            EnsureLocalDevelopmentEnvironment();
            await context.Database.MigrateAsync();
            Console.WriteLine("SQL migrations applied successfully.");
            break;
        case RunnerMode.VerifyRelease:
        case RunnerMode.ApplyRelease:
            var descriptor = await LoadReleaseDescriptorAsync(runnerArguments.DescriptorPath!);
            await context.Database.OpenConnectionAsync();
            var releaseGuardAcquired = false;
            try
            {
                await AcquireReleaseMigrationGuardAsync(context);
                releaseGuardAcquired = true;

                var verification = await VerifyReleaseAsync(context, descriptor);
                if (runnerArguments.Mode == RunnerMode.ApplyRelease)
                {
                    await ApplyOutstandingMigrationsAsync(context, verification.Outstanding);
                    verification = await VerifyReleaseAsync(context, descriptor);
                }

                WriteReleaseEvidence(descriptor, verification, runnerArguments.Mode);
            }
            finally
            {
                if (releaseGuardAcquired)
                {
                    await ReleaseReleaseMigrationGuardAsync(context);
                }

                await context.Database.CloseConnectionAsync();
            }
            break;
        default:
            throw new InvalidOperationException("Unsupported migration runner mode.");
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"SQL migration failed: category={GetFailureCategory(exception)}; exception={exception.GetType().Name}; detail={GetSafeFailureDetail(exception, connectionString)}.");
    return 1;
}

static RunnerArguments ParseArguments(string[] arguments) => arguments switch
{
    [] => new RunnerArguments(RunnerMode.LocalDevelopment, null),
    ["--ownership-precondition"] => new RunnerArguments(RunnerMode.OwnershipPrecondition, null),
    ["--verify-release", var path] when !string.IsNullOrWhiteSpace(path) => new RunnerArguments(RunnerMode.VerifyRelease, path),
    ["--apply-release", var path] when !string.IsNullOrWhiteSpace(path) => new RunnerArguments(RunnerMode.ApplyRelease, path),
    _ => throw new ArgumentException("Invalid migration runner arguments.")
};

static void EnsureLocalDevelopmentEnvironment()
{
    var environment = Environment.GetEnvironmentVariable("DEPLOYMENT_ENVIRONMENT");
    if (!string.IsNullOrEmpty(environment) && !string.Equals(environment, "development", StringComparison.Ordinal))
    {
        throw new MigrationStateConflictException("Local migration mode is permitted only for the development environment.");
    }
}

static async Task RunOwnershipPreconditionAsync(CloudOrdersDbContext context) =>
    await context.Database.ExecuteSqlRawAsync("""
        SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;
        DECLARE @invalid bigint = (SELECT COUNT_BIG(*) FROM dbo.Orders WHERE CustomerProfileId IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.IdempotencyRecords WHERE ActorCustomerProfileId IS NULL)
          + (SELECT COUNT_BIG(*) FROM dbo.IdempotencyRecords WHERE TargetCustomerProfileId IS NULL)
          + (SELECT COUNT_BIG(*) FROM (SELECT ActorCustomerProfileId, IdempotencyKey FROM dbo.IdempotencyRecords GROUP BY ActorCustomerProfileId, IdempotencyKey HAVING COUNT_BIG(*) > 1) AS duplicateGroups);
        IF @invalid <> 0 THROW 51001, 'Sprint 4B ownership precondition failed; migration is blocked.', 1;
        ROLLBACK TRANSACTION;
        """);

static async Task<LoadedReleaseDescriptor> LoadReleaseDescriptorAsync(string descriptorPath)
{
    try
    {
        var resolvedPath = Path.GetFullPath(descriptorPath);
        var descriptorBytes = await File.ReadAllBytesAsync(resolvedPath);
        using var descriptorDocument = JsonDocument.Parse(descriptorBytes);
        ValidateReleaseDescriptorShape(descriptorDocument.RootElement);
        var descriptor = JsonSerializer.Deserialize<ReleaseDescriptor>(descriptorBytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new ReleaseDescriptorInvalidException("Release descriptor is empty.");

        ValidateReleaseDescriptor(descriptor);
        return new LoadedReleaseDescriptor(descriptor, Convert.ToHexString(SHA256.HashData(descriptorBytes)).ToLowerInvariant());
    }
    catch (ReleaseDescriptorInvalidException)
    {
        throw;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
    {
        throw new ReleaseDescriptorInvalidException("Release descriptor could not be loaded.");
    }
}

static void ValidateReleaseDescriptor(ReleaseDescriptor descriptor)
{
    if (string.IsNullOrWhiteSpace(descriptor.ReleaseId) || descriptor.SchemaVersion != 1 ||
        descriptor.Environments is null || descriptor.RequiredMigrationBaseline is null || descriptor.AuthorisedMigrations is null ||
        descriptor.Environments.Length == 0 || descriptor.RequiredMigrationBaseline.Length == 0 ||
        !descriptor.Environments.All(environment => environment is "development" or "test") ||
        !descriptor.Environments.Distinct(StringComparer.Ordinal).SequenceEqual(descriptor.Environments, StringComparer.Ordinal) ||
        !descriptor.RequiredMigrationBaseline.All(IsFullMigrationId) ||
        !descriptor.AuthorisedMigrations.All(IsFullMigrationId) ||
        !descriptor.RequiredMigrationBaseline.Distinct(StringComparer.Ordinal).SequenceEqual(descriptor.RequiredMigrationBaseline, StringComparer.Ordinal) ||
        !descriptor.AuthorisedMigrations.Distinct(StringComparer.Ordinal).SequenceEqual(descriptor.AuthorisedMigrations, StringComparer.Ordinal) ||
        !descriptor.AuthorisedMigrations.SequenceEqual(
            descriptor.RequiredMigrationBaseline.Take(descriptor.AuthorisedMigrations.Length),
            StringComparer.Ordinal) ||
        descriptor.Precondition is not ("none" or "ownership-read-only-transaction") ||
        descriptor.TrafficPolicy is not ("none" or "controlled-nonproduction-access") ||
        descriptor.Compatibility is not ("api-compatible" or "maintenance-required"))
    {
        throw new ReleaseDescriptorInvalidException("Release descriptor has invalid required fields.");
    }

    var environment = Environment.GetEnvironmentVariable("DEPLOYMENT_ENVIRONMENT");
    if (string.IsNullOrWhiteSpace(environment) || !descriptor.Environments.Contains(environment, StringComparer.Ordinal))
    {
        throw new ReleaseDescriptorInvalidException("Release descriptor does not permit the deployment environment.");
    }
}

static void ValidateReleaseDescriptorShape(JsonElement root)
{
    if (root.ValueKind != JsonValueKind.Object)
    {
        throw new ReleaseDescriptorInvalidException("Release descriptor must be a JSON object.");
    }

    var expectedProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "releaseId",
        "schemaVersion",
        "environments",
        "deployApi",
        "requiredMigrationBaseline",
        "authorisedMigrations",
        "precondition",
        "trafficPolicy",
        "compatibility"
    };
    var seenProperties = new HashSet<string>(StringComparer.Ordinal);
    foreach (var property in root.EnumerateObject())
    {
        if (!expectedProperties.Contains(property.Name) || !seenProperties.Add(property.Name) ||
            !HasExpectedJsonShape(property.Name, property.Value))
        {
            throw new ReleaseDescriptorInvalidException("Release descriptor has an invalid JSON shape.");
        }
    }

    if (!expectedProperties.SetEquals(seenProperties))
    {
        throw new ReleaseDescriptorInvalidException("Release descriptor is missing required properties.");
    }
}

static bool HasExpectedJsonShape(string propertyName, JsonElement value) => propertyName switch
{
    "releaseId" or "precondition" or "trafficPolicy" or "compatibility" => value.ValueKind == JsonValueKind.String,
    "schemaVersion" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
    "deployApi" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
    "environments" or "requiredMigrationBaseline" or "authorisedMigrations" =>
        value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
    _ => false
};

static bool IsFullMigrationId(string migrationId) =>
    migrationId.Length > 16 &&
    migrationId[14] == '_' &&
    migrationId.Take(14).All(char.IsAsciiDigit) &&
    char.IsAsciiLetter(migrationId[15]) &&
    migrationId[16..].All(character => char.IsAsciiLetterOrDigit(character));

static async Task AcquireReleaseMigrationGuardAsync(CloudOrdersDbContext context)
{
    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = """
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = N'CloudOrders.ReleaseMigrationRunner', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 60000;
        SELECT @result;
        """;
    var result = await command.ExecuteScalarAsync(CancellationToken.None);
    if (result is not int status || status < 0)
    {
        throw new MigrationStateConflictException("Release migration guard could not be acquired.");
    }
}

static async Task ReleaseReleaseMigrationGuardAsync(CloudOrdersDbContext context)
{
    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = "EXEC sp_releaseapplock @Resource = N'CloudOrders.ReleaseMigrationRunner', @LockOwner = N'Session';";
    await command.ExecuteNonQueryAsync(CancellationToken.None);
}

static async Task<ReleaseVerification> VerifyReleaseAsync(CloudOrdersDbContext context, LoadedReleaseDescriptor loadedDescriptor)
{
    var knownMigrations = context.Database.GetMigrations().ToArray();
    var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();
    var baseline = loadedDescriptor.Descriptor.RequiredMigrationBaseline;

    if (!baseline.SequenceEqual(knownMigrations.Take(baseline.Length), StringComparer.Ordinal) ||
        !appliedMigrations.All(knownMigrations.Contains) ||
        !pendingMigrations.All(knownMigrations.Contains))
    {
        throw new MigrationBaselineConflictException("Release baseline does not match known EF migrations.");
    }

    if (appliedMigrations.Length > baseline.Length ||
        !appliedMigrations.SequenceEqual(baseline.Take(appliedMigrations.Length), StringComparer.Ordinal))
    {
        throw new MigrationBaselineConflictException("Applied migration history is not an ordered release baseline prefix.");
    }

    var outstanding = baseline.Skip(appliedMigrations.Length).ToArray();
    if (!outstanding.All(loadedDescriptor.Descriptor.AuthorisedMigrations.Contains))
    {
        throw new MigrationStateConflictException("Release has an unauthorised outstanding migration.");
    }

    return new ReleaseVerification(appliedMigrations, baseline, outstanding);
}

static async Task ApplyOutstandingMigrationsAsync(CloudOrdersDbContext context, IReadOnlyList<string> outstanding)
{
    if (outstanding.Count == 0)
    {
        return;
    }

    var appliedMigrationsBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    var migrator = context.GetService<IMigrator>();
    foreach (var migrationId in outstanding)
    {
        await migrator.MigrateAsync(migrationId);
    }

    var appliedMigrationsAfter = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    if (appliedMigrationsAfter.Length < appliedMigrationsBefore.Length ||
        !appliedMigrationsAfter.Take(appliedMigrationsBefore.Length).SequenceEqual(appliedMigrationsBefore, StringComparer.Ordinal) ||
        !appliedMigrationsAfter.Skip(appliedMigrationsBefore.Length).SequenceEqual(outstanding, StringComparer.Ordinal))
    {
        throw new MigrationStateConflictException("Release application produced an unexpected migration history delta.");
    }
}

static void WriteReleaseEvidence(LoadedReleaseDescriptor loadedDescriptor, ReleaseVerification verification, RunnerMode mode) =>
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        releaseId = loadedDescriptor.Descriptor.ReleaseId,
        descriptorSha256 = loadedDescriptor.Sha256,
        mode = mode == RunnerMode.VerifyRelease ? "verify" : "apply",
        applied = verification.Applied,
        baseline = verification.Baseline,
        outstanding = verification.Outstanding
    }));

static string GetFailureCategory(Exception exception) => exception switch
{
    ReleaseDescriptorInvalidException => "RELEASE_DESCRIPTOR_INVALID",
    MigrationBaselineConflictException => "MIGRATION_BASELINE_CONFLICT",
    MigrationStateConflictException => "MIGRATION_STATE_CONFLICT",
    SqlException => "SQL_CONNECTION_OR_AUTHORIZATION",
    _ => "UNEXPECTED"
};

static string GetSafeFailureDetail(Exception exception, string connectionString) => exception switch
{
    ReleaseDescriptorInvalidException or MigrationBaselineConflictException or MigrationStateConflictException => exception.Message,
    SqlException sqlException => $"SqlErrorNumber={sqlException.Number}; SqlErrorState={sqlException.State}; SqlErrorClass={sqlException.Class}",
    InvalidOperationException => exception.Message.Replace(connectionString, "[REDACTED]", StringComparison.Ordinal),
    _ => "Unavailable"
};

file enum RunnerMode
{
    LocalDevelopment,
    OwnershipPrecondition,
    VerifyRelease,
    ApplyRelease
}

file sealed record RunnerArguments(RunnerMode Mode, string? DescriptorPath);

file sealed record ReleaseDescriptor(
    string ReleaseId,
    int SchemaVersion,
    string[] Environments,
    bool DeployApi,
    string[] RequiredMigrationBaseline,
    string[] AuthorisedMigrations,
    string Precondition,
    string TrafficPolicy,
    string Compatibility);

file sealed record LoadedReleaseDescriptor(ReleaseDescriptor Descriptor, string Sha256);

file sealed record ReleaseVerification(string[] Applied, string[] Baseline, string[] Outstanding);

file sealed class ReleaseDescriptorInvalidException(string message) : Exception(message);

file sealed class MigrationBaselineConflictException(string message) : Exception(message);

file sealed class MigrationStateConflictException(string message) : Exception(message);
