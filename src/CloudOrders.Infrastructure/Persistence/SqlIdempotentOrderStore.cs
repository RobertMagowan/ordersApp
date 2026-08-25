using System.Text.Json;
using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;
using CloudOrders.Contracts.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class SqlIdempotentOrderStore(
    IDbContextFactory<CloudOrdersDbContext> contextFactory,
    TimeProvider timeProvider) : IIdempotentOrderStore
{
    private const string IdempotencyPrimaryKeySqlIdentifier = "'PK_IdempotencyRecords'";
    private const string IdempotencyTableSqlIdentifier = "'dbo.IdempotencyRecords'";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromDays(7);

    public async Task<CreateOrderResult> CreateAsync(
        IdempotentOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var existing = await FindExistingAsync(context, request, cancellationToken);
        if (existing is not null && existing.ExpiresAt > now)
        {
            return Classify(existing, request.RequestHash);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (existing is not null)
        {
            await context.IdempotencyRecords
                .Where(record => record.SubjectId == request.SubjectId
                    && record.IdempotencyKey == request.IdempotencyKey
                    && record.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
        }
        context.Orders.Add(OrderPersistenceMapper.ToEntity(request.Order));
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
            var racedRecord = await FindExistingInNewQueryAsync(request, cancellationToken);
            if (racedRecord is null)
            {
                throw;
            }

            return Classify(racedRecord, request.RequestHash);
        }
    }

    private async Task<IdempotencyRecordEntity?> FindExistingInNewQueryAsync(
        IdempotentOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindExistingAsync(context, request, cancellationToken);
    }

    private static Task<IdempotencyRecordEntity?> FindExistingAsync(
        CloudOrdersDbContext context,
        IdempotentOrderRequest request,
        CancellationToken cancellationToken) =>
        context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.SubjectId == request.SubjectId && record.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

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

    private static OutboxMessageEntity ToOutboxEntity(IdempotentOrderRequest request) =>
        new()
        {
            EventId = request.IntegrationEvent.EventId,
            OrderId = request.Order.Id,
            AggregateId = request.Order.Id,
            MessageType = OrderCreatedIntegrationEventV1.MessageType,
            MessageVersion = OrderCreatedIntegrationEventV1.CurrentMessageVersion,
            Payload = JsonSerializer.Serialize(request.IntegrationEvent, JsonOptions),
            OccurredAt = request.IntegrationEvent.OccurredAt,
            CreatedAt = request.Order.CreatedAt,
            AttemptCount = 0,
            TraceParent = request.TraceParent
        };

    private static IdempotencyRecordEntity ToIdempotencyEntity(IdempotentOrderRequest request) =>
        new()
        {
            SubjectId = request.SubjectId,
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
