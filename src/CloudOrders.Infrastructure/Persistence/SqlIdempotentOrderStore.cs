using System.Text.Json;
using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class SqlIdempotentOrderStore(
    IDbContextFactory<CloudOrdersDbContext> contextFactory,
    TimeProvider timeProvider) : IIdempotentOrderStore, IOutboxLeaseStore
{
    private const string IdempotencyPrimaryKeySqlIdentifier = "'PK_IdempotencyRecords'";
    private const string IdempotencyTableSqlIdentifier = "'dbo.IdempotencyRecords'";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<OutboxMessageLease>> ClaimAsync(string leaseOwner, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || batchSize is < 1 or > 500 || leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Lease owner, batch size, and duration are invalid.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var token = Guid.NewGuid();
        var rows = new List<OutboxMessageLease>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            UPDATE TOP (@batchSize) o WITH (UPDLOCK, READPAST, ROWLOCK)
            SET LeaseOwner = @owner, LeaseToken = @token,
                LeaseExpiresAt = DATEADD(second, @durationSeconds, SYSUTCDATETIME()),
                AttemptCount = AttemptCount + 1, LastAttemptAt = SYSUTCDATETIME()
            OUTPUT inserted.EventId, inserted.OrderId, inserted.MessageType, inserted.MessageVersion,
                   inserted.Payload, inserted.TraceParent, inserted.AttemptCount,
                   inserted.LeaseOwner, inserted.LeaseToken, inserted.LeaseExpiresAt
            FROM dbo.OutboxMessages AS o
            WHERE ProcessedAt IS NULL AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt <= SYSUTCDATETIME());
            """;
        AddParameter(command, "batchSize", batchSize);
        AddParameter(command, "owner", leaseOwner);
        AddParameter(command, "token", token);
        AddParameter(command, "durationSeconds", Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds)));
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt32(6), reader.GetString(7), reader.GetGuid(8), reader.GetFieldValue<DateTimeOffset>(9)));
        return rows;
    }

    public async Task<bool> MarkPublishedAsync(Guid eventId, string leaseOwner, Guid leaseToken, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.OutboxMessages.Where(x => x.EventId == eventId && x.ProcessedAt == null && x.LeaseOwner == leaseOwner && x.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProcessedAt, timeProvider.GetUtcNow()).SetProperty(x => x.LeaseOwner, (string?)null).SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken) == 1;
    }

    public async Task<bool> RenewAsync(Guid eventId, string leaseOwner, Guid leaseToken, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var seconds = Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds));
        return await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET LeaseExpiresAt = DATEADD(second, {seconds}, SYSUTCDATETIME()) WHERE EventId = {eventId} AND ProcessedAt IS NULL AND LeaseOwner = {leaseOwner} AND LeaseToken = {leaseToken} AND LeaseExpiresAt > SYSUTCDATETIME()", cancellationToken) == 1;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = "@" + name; parameter.Value = value; command.Parameters.Add(parameter);
    }

    public async Task<CreateOrderResult> CreateAsync(
        IdempotentOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var actorCustomerProfileId = GetRequiredActorCustomerProfileId(request);
        var existing = await FindExistingAsync(context, request, actorCustomerProfileId, cancellationToken);
        if (existing is not null && existing.ExpiresAt > now)
        {
            return Classify(existing, request.RequestHash);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (existing is not null)
        {
            await context.IdempotencyRecords
                .Where(record => record.ActorCustomerProfileId == actorCustomerProfileId
                    && record.IdempotencyKey == request.IdempotencyKey
                    && record.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
        }
        context.Orders.Add(OrderPersistenceMapper.ToEntity(request.Order, request.TargetCustomerProfileId));
        context.OutboxMessages.Add(ToOutboxEntity(request));
        context.IdempotencyRecords.Add(ToIdempotencyEntity(request));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreateOrderResult.Created(request.Response);
        }
        catch (DbUpdateException exception) when (IsIdempotencyPrimaryKeyViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            var racedRecord = await FindExistingInNewQueryAsync(request, actorCustomerProfileId, cancellationToken);
            if (racedRecord is null)
            {
                throw;
            }

            return Classify(racedRecord, request.RequestHash);
        }
    }

    private async Task<IdempotencyRecordEntity?> FindExistingInNewQueryAsync(
        IdempotentOrderRequest request,
        Guid actorCustomerProfileId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindExistingAsync(context, request, actorCustomerProfileId, cancellationToken);
    }

    private static Task<IdempotencyRecordEntity?> FindExistingAsync(
        CloudOrdersDbContext context,
        IdempotentOrderRequest request,
        Guid actorCustomerProfileId,
        CancellationToken cancellationToken) =>
        context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.ActorCustomerProfileId == actorCustomerProfileId
                    && record.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

    private static Guid GetRequiredActorCustomerProfileId(IdempotentOrderRequest request) =>
        request.ActorCustomerProfileId
            ?? throw new InvalidOperationException("Actor ownership is required for SQL persistence.");

    private static CreateOrderResult Classify(
        IdempotencyRecordEntity existing,
        byte[] requestHash)
    {
        if (!existing.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return CreateOrderResult.Conflict();
        }

        var response = JsonSerializer.Deserialize<OrderResponse>(existing.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("The stored idempotency response is invalid.");
        return CreateOrderResult.Replayed(response);
    }

    private static OutboxMessageEntity ToOutboxEntity(IdempotentOrderRequest request)
    {
        var source = request.IntegrationEvent;
        var canonicalEvent = new OrderCreatedIntegrationEventV1(
            source.EventId,
            source.OrderId,
            source.CustomerReference,
            source.ProductSku,
            source.Quantity,
            source.OccurredAt)
        {
            TraceParent = request.TraceParent
        };

        return new()
        {
            EventId = canonicalEvent.EventId,
            OrderId = request.Order.Id,
            AggregateId = request.Order.Id,
            MessageType = OrderCreatedIntegrationEventV1.MessageType,
            MessageVersion = OrderCreatedIntegrationEventV1.CurrentMessageVersion,
            Payload = JsonSerializer.Serialize(canonicalEvent, JsonOptions),
            OccurredAt = canonicalEvent.OccurredAt,
            CreatedAt = request.Order.CreatedAt,
            AttemptCount = 0,
            TraceParent = request.TraceParent
        };
    }

    private static IdempotencyRecordEntity ToIdempotencyEntity(IdempotentOrderRequest request) =>
        new()
        {
            SubjectId = request.SubjectId,
            ActorCustomerProfileId = GetRequiredActorCustomerProfileId(request),
            TargetCustomerProfileId = request.TargetCustomerProfileId
                ?? throw new InvalidOperationException("Target ownership is required for SQL persistence."),
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = request.RequestHash,
            OrderId = request.Order.Id,
            ResponseStatus = 201,
            ResponseJson = JsonSerializer.Serialize(request.Response, JsonOptions),
            CreatedAt = request.Order.CreatedAt,
            ExpiresAt = request.Order.CreatedAt.Add(IdempotencyRetention)
        };

    private static bool IsIdempotencyPrimaryKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
        && sqlException.Errors.Cast<SqlError>().Any(error =>
            error.Number == 2627
            && error.Message.Contains(IdempotencyPrimaryKeySqlIdentifier, StringComparison.Ordinal)
            && error.Message.Contains(IdempotencyTableSqlIdentifier, StringComparison.Ordinal));
}
