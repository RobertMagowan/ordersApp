using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CloudOrders.IntegrationTests;

internal sealed class IdempotencyRaceObserver
{
    private const string IdempotencyLookupSql = "FROM [dbo].[IdempotencyRecords] AS [i]";
    private static readonly string[] TransactionInsertSql =
    [
        "INSERT INTO [dbo].[Orders]",
        "INSERT INTO [dbo].[OutboxMessages]",
        "INSERT INTO [dbo].[IdempotencyRecords]"
    ];

    private readonly TaskCompletionSource initialLookupsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<Guid, byte> lookupContextIds = [];
    private readonly int synchronizedLookupCount;
    private int idempotencyLookupCount;
    private int initialEmptyLookupCount;
    private int lookupCountAtFirstInsert = -1;
    private int rollbackCount;

    public IdempotencyRaceObserver(int synchronizedLookupCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(synchronizedLookupCount, 1);
        this.synchronizedLookupCount = synchronizedLookupCount;
        CommandInterceptor = new RaceCommandInterceptor(this);
        TransactionInterceptor = new RaceTransactionInterceptor(this);
    }

    public IInterceptor CommandInterceptor { get; }

    public IInterceptor TransactionInterceptor { get; }

    public int IdempotencyLookupCount => Volatile.Read(ref idempotencyLookupCount);

    public int InitialEmptyLookupCount => Volatile.Read(ref initialEmptyLookupCount);

    public int LookupContextCount => lookupContextIds.Count;

    public int LookupCountAtFirstInsert => Volatile.Read(ref lookupCountAtFirstInsert);

    public int RollbackCount => Volatile.Read(ref rollbackCount);

    private async ValueTask<InterceptionResult> ObserveReaderClosingAsync(
        DbCommand command,
        DataReaderClosingEventData eventData,
        InterceptionResult result)
    {
        if (!command.CommandText.Contains(IdempotencyLookupSql, StringComparison.Ordinal))
        {
            return result;
        }

        var contextId = eventData.Context?.ContextId.InstanceId;
        if (contextId.HasValue)
        {
            lookupContextIds.TryAdd(contextId.Value, 0);
        }

        var completedLookupCount = Interlocked.Increment(ref idempotencyLookupCount);
        if (completedLookupCount <= synchronizedLookupCount)
        {
            if (!eventData.DataReader.HasRows)
            {
                Interlocked.Increment(ref initialEmptyLookupCount);
            }

            if (completedLookupCount == synchronizedLookupCount)
            {
                initialLookupsCompleted.TrySetResult();
            }

            await initialLookupsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        return result;
    }

    private ValueTask<InterceptionResult<DbDataReader>> ObserveReaderExecutingAsync(
        DbCommand command,
        InterceptionResult<DbDataReader> result)
    {
        if (TransactionInsertSql.Any(sql => command.CommandText.Contains(sql, StringComparison.Ordinal)))
        {
            Interlocked.CompareExchange(
                ref lookupCountAtFirstInsert,
                Volatile.Read(ref idempotencyLookupCount),
                -1);
        }

        return ValueTask.FromResult(result);
    }

    private Task ObserveTransactionRolledBackAsync()
    {
        Interlocked.Increment(ref rollbackCount);
        return Task.CompletedTask;
    }

    private sealed class RaceCommandInterceptor(IdempotencyRaceObserver observer) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command,
            DataReaderClosingEventData eventData,
            InterceptionResult result) =>
            observer.ObserveReaderClosingAsync(command, eventData, result);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            observer.ObserveReaderExecutingAsync(command, result);

    }

    private sealed class RaceTransactionInterceptor(IdempotencyRaceObserver observer) : DbTransactionInterceptor
    {
        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            observer.ObserveTransactionRolledBackAsync();
    }
}
