using Centra.Drivers;
using Centra.Providers.InMemory.State;
using Centra.Registry;
using Centra.Serialization;
using Centra.State;
using Centra.Tests.Unit.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.State;

public sealed class StateBatchTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IStateStoreDriver _driver = Substitute.For<IStateStoreDriver>();
    private readonly ICentraSerializer _serializer = JsonCentraSerializer.Default;

    public StateBatchTests()
    {
        _registry.RegisterStateStoreDriver("orders-state", _driver);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Get_Batch_State_When_Keys_Exist(
        string key1,
        string key2,
        string orderId1,
        string orderId2)
    {
        // Arrange
        var order1 = new TestStateOrder(orderId1, "prod-1", 10);
        var order2 = new TestStateOrder(orderId2, "prod-2", 20);

        var rawEntries = new[]
        {
            new StateEntry<byte[]>(key1, _serializer.Serialize(order1), "etag-1"),
            new StateEntry<byte[]>(key2, _serializer.Serialize(order2), "etag-2")
        };

        _driver.GetBatchAsync("orders-state", Arg.Is<IReadOnlyList<string>>(k => k.Contains(key1) && k.Contains(key2)), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<StateEntry<byte[]>>>(rawEntries));

        var stateStore = new CentraStateStore(_registry, _serializer);

        // Act
        var results = await stateStore.GetBatchAsync<TestStateOrder>("orders-state", [key1, key2]);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Key.ShouldBe(key1);
        results[0].Value.OrderId.ShouldBe(orderId1);
        results[1].Key.ShouldBe(key2);
        results[1].Value.OrderId.ShouldBe(orderId2);
    }

    [Fact]
    public async Task Should_Return_Empty_When_Keys_List_Is_Empty()
    {
        // Arrange
        var stateStore = new CentraStateStore(_registry, _serializer);

        // Act
        var results = await stateStore.GetBatchAsync<TestStateOrder>("orders-state", Array.Empty<string>());

        // Assert
        results.ShouldBeEmpty();
        await _driver.DidNotReceiveWithAnyArgs().GetBatchAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Should_Get_Batch_Using_InMemoryStateStoreDriver()
    {
        // Arrange
        var inMemoryDriver = new InMemoryStateStoreDriver();
        var registry = new ComponentRegistry();
        registry.RegisterStateStoreDriver("mem-store", inMemoryDriver);

        var stateStore = new CentraStateStore(registry, _serializer);
        var typedStore = new CentraStateStore<TestStateOrder>(stateStore, "mem-store");

        await typedStore.SetAsync("item:1", new TestStateOrder("ord-1", "p1", 100));
        await typedStore.SetAsync("item:2", new TestStateOrder("ord-2", "p2", 200));

        // Act - query 3 keys, where item:3 does not exist
        var batch = await typedStore.GetBatchAsync(["item:1", "item:2", "item:3"]);

        // Assert
        batch.Count.ShouldBe(2);
        batch.ShouldContain(e => e.Key == "item:1" && e.Value.OrderId == "ord-1");
        batch.ShouldContain(e => e.Key == "item:2" && e.Value.OrderId == "ord-2");
    }
}
