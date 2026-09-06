using CloudOrders.Application.Abstractions;
using CloudOrders.Application.Orders;

namespace CloudOrders.UnitTests;

public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task HandleBindsIdempotencyToBothActorAndTargetProfiles()
    {
        var store = new RecordingIdempotentOrderStore();
        var handler = new CreateOrderHandler(store, TimeProvider.System);
        var actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var target = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var result = await handler.Handle(
            new CreateOrderCommand("CUST-001", "SKU-001", 2),
            actor,
            target,
            Guid.NewGuid(),
            traceParent: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(CreateOrderResultKind.Created, result.Kind);
        Assert.NotNull(store.Request);
        Assert.Equal($"profile:{actor:N}", store.Request.SubjectId);
        Assert.Equal(actor, store.Request.ActorCustomerProfileId);
        Assert.Equal(target, store.Request.TargetCustomerProfileId);
    }

    [Fact]
    public async Task HandleUsesDifferentIdempotencyHashesForDifferentTargets()
    {
        var actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var firstTarget = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var secondTarget = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var firstStore = new RecordingIdempotentOrderStore();
        var secondStore = new RecordingIdempotentOrderStore();
        var firstHandler = new CreateOrderHandler(firstStore, TimeProvider.System);
        var secondHandler = new CreateOrderHandler(secondStore, TimeProvider.System);

        await firstHandler.Handle(
            new CreateOrderCommand("CUST-001", "SKU-001", 2), actor, firstTarget, Guid.NewGuid(), null, CancellationToken.None);
        await secondHandler.Handle(
            new CreateOrderCommand("CUST-001", "SKU-001", 2), actor, secondTarget, Guid.NewGuid(), null, CancellationToken.None);

        Assert.NotEqual(firstStore.Request!.RequestHash, secondStore.Request!.RequestHash);
    }

    [Fact]
    public async Task HandleCanonicalizesAndPersistsIdempotentOrderSlice()
    {
        var store = new RecordingIdempotentOrderStore();
        var handler = new CreateOrderHandler(
            store,
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

}
