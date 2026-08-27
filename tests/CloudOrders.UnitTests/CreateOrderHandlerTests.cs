using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;

namespace CloudOrders.UnitTests;

public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task HandleCanonicalizesAndPersistsIdempotentOrderSlice()
    {
        var store = new RecordingIdempotentOrderStore();
        var handler = new CreateOrderHandler(
            store,
            new StubSubjectIdProvider("local-development-subject"),
            TimeProvider.System);
        var idempotencyKey = Guid.NewGuid();

        var result = await handler.Handle(
            new CreateOrderCommand(" cust-001 ", " sku-001 ", 2),
            idempotencyKey,
            traceParent: null,
            CancellationToken.None);

        Assert.Equal(CreateOrderResultKind.Created, result.Kind);
        Assert.NotNull(result.Response);
        Assert.Equal("CUST-001", result.Response.CustomerReference);
        Assert.NotNull(store.Request);
        Assert.Equal("local-development-subject", store.Request.SubjectId);
        Assert.Equal(idempotencyKey, store.Request.IdempotencyKey);
        Assert.Equal(result.Response.Id, store.Request.IntegrationEvent.OrderId);
        Assert.Equal(
            "75ad5b019fad99f92b331201a6faa101d60899ad36c7bac025fd0ffa6df12616",
            Convert.ToHexString(store.Request.RequestHash).ToLowerInvariant());
    }

    [Fact]
    public async Task HandleReturnsValidationResultWithoutCallingStore()
    {
        var store = new RecordingIdempotentOrderStore();
        var handler = new CreateOrderHandler(
            store,
            new StubSubjectIdProvider("local-development-subject"),
            TimeProvider.System);

        var result = await handler.Handle(
            new CreateOrderCommand("CUST-001", "SKU-001", 0),
            Guid.NewGuid(),
            traceParent: null,
            CancellationToken.None);

        Assert.Equal(CreateOrderResultKind.ValidationError, result.Kind);
        Assert.Null(store.Request);
    }

    private sealed class RecordingIdempotentOrderStore : IIdempotentOrderStore
    {
        public IdempotentOrderRequest? Request { get; private set; }

        public Task<CreateOrderResult> CreateAsync(
            IdempotentOrderRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(CreateOrderResult.Created(request.Response));
        }
    }

    private sealed class StubSubjectIdProvider(string subjectId) : ISubjectIdProvider
    {
        public string SubjectId { get; } = subjectId;
    }
}
