using Centra.Drivers;
using Centra.Registry;
using Centra.Serialization;
using Centra.State;
using Centra.Tests.Unit.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.State;

public sealed class StateStoreTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IStateStoreDriver _driver = Substitute.For<IStateStoreDriver>();
    private readonly ICentraSerializer _serializer = JsonCentraSerializer.Default;

    public StateStoreTests()
    {
        _registry.RegisterStateStoreDriver("orders-state", _driver);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Get_State_When_Key_Exists(
        string key,
        string orderId,
        string productId,
        int quantity,
        string etag)
    {
        // Arrange
        var expectedOrder = new TestStateOrder(orderId, productId, quantity);
        var bytes = _serializer.Serialize(expectedOrder);
        var entry = new StateEntry<byte[]>(key, bytes, etag);

        _driver.GetAsync("orders-state", key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)entry));

        var stateStore = new CentraStateStore(_registry, _serializer);

        // Act
        var result = await stateStore.GetAsync<TestStateOrder>("orders-state", key);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Key.ShouldBe(key);
        result.Value.ETag.ShouldBe(etag);
        result.Value.Value.OrderId.ShouldBe(orderId);
        result.Value.Value.ProductId.ShouldBe(productId);
        result.Value.Value.Quantity.ShouldBe(quantity);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Return_Null_When_Key_Not_Found(string key)
    {
        // Arrange
        _driver.GetAsync("orders-state", key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)null));

        var stateStore = new CentraStateStore(_registry, _serializer);

        // Act
        var result = await stateStore.GetAsync<TestStateOrder>("orders-state", key);

        // Assert
        result.ShouldBeNull();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Set_State_And_Call_Driver(
        string key,
        string orderId,
        string productId,
        int quantity)
    {
        // Arrange
        var order = new TestStateOrder(orderId, productId, quantity);
        var stateStore = new CentraStateStore(_registry, _serializer);

        // Act
        await stateStore.SetAsync("orders-state", key, order);

        // Assert
        await _driver.Received(1).SetAsync(
            "orders-state",
            key,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_TrySet_With_ExpectedETag(
        string key,
        string orderId,
        string productId,
        int quantity,
        string etag)
    {
        // Arrange
        var order = new TestStateOrder(orderId, productId, quantity);
        _driver.TrySetAsync("orders-state", key, Arg.Any<ReadOnlyMemory<byte>>(), etag, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var stateStore = new CentraStateStore(_registry, _serializer);

        // Act
        var success = await stateStore.TrySetAsync("orders-state", key, order, etag);

        // Assert
        success.ShouldBeTrue();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Support_Generic_Facade_IStateStoreT(
        string key,
        string orderId,
        string productId,
        int quantity,
        string etag)
    {
        // Arrange
        var expectedOrder = new TestStateOrder(orderId, productId, quantity);
        var bytes = _serializer.Serialize(expectedOrder);
        var entry = new StateEntry<byte[]>(key, bytes, etag);

        _driver.GetAsync("orders-state", key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)entry));

        var stateStore = new CentraStateStore(_registry, _serializer);
        var typedStore = new CentraStateStore<TestStateOrder>(stateStore, "orders-state");

        // Act
        var result = await typedStore.GetAsync(key);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Value.OrderId.ShouldBe(orderId);
    }
}
