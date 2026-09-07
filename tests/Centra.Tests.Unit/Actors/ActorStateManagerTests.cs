using Centra.Actors;
using Centra.Core.Actors;
using Centra.State;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorStateManagerTests
{
    private readonly IStateStore _stateStore;
    private readonly ActorIdentity _identity;
    private readonly string _storeName = "statestore";

    public ActorStateManagerTests()
    {
        _stateStore = Substitute.For<IStateStore>();
        _identity = new ActorIdentity("AccountActor", "acc-101");
    }

    [Fact]
    public async Task Should_Get_And_Cache_State_From_StateStore()
    {
        // Arrange
        var key = "actors:AccountActor:acc-101:balance";
        _stateStore.GetAsync<decimal>(_storeName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<decimal>("acc-101", 100m, "etag-1"));

        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);

        // Act
        var balance1 = await stateManager.GetStateAsync<decimal>("balance");
        var balance2 = await stateManager.GetStateAsync<decimal>("balance");

        // Assert
        balance1.ShouldBe(100m);
        balance2.ShouldBe(100m);

        // Verify state store was called only once (cached on second read)
        await _stateStore.Received(1).GetAsync<decimal>(_storeName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Track_Dirty_States_And_Flush_On_SaveState()
    {
        // Arrange
        var key = "actors:AccountActor:acc-101:balance";
        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);

        // Act
        await stateManager.SetStateAsync("balance", 250m);
        await stateManager.SaveStateAsync();

        // Assert
        await _stateStore.Received(1).SetAsync(
            _storeName,
            key,
            250m,
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Use_Optimistic_Concurrency_ETag_When_Updating_Existing_State()
    {
        // Arrange
        var key = "actors:AccountActor:acc-101:balance";
        _stateStore.GetAsync<decimal>(_storeName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<decimal>("acc-101", 100m, "etag-initial"));

        _stateStore.TrySetAsync(_storeName, key, 150m, "etag-initial", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);

        // Act
        var current = await stateManager.GetStateAsync<decimal>("balance");
        current.ShouldBe(100m);

        await stateManager.SetStateAsync("balance", 150m);
        await stateManager.SaveStateAsync();

        // Assert
        await _stateStore.Received(1).TrySetAsync(
            _storeName,
            key,
            150m,
            "etag-initial",
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Throw_ActorConcurrencyException_When_ETag_Mismatch_Occurs()
    {
        // Arrange
        var key = "actors:AccountActor:acc-101:balance";
        _stateStore.GetAsync<decimal>(_storeName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<decimal>("acc-101", 100m, "etag-old"));

        _stateStore.TrySetAsync(_storeName, key, 200m, "etag-old", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(false); // ETag conflict simulation

        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);

        // Act & Assert
        await stateManager.GetStateAsync<decimal>("balance");
        await stateManager.SetStateAsync("balance", 200m);

        var ex = await Should.ThrowAsync<ActorConcurrencyException>(() => stateManager.SaveStateAsync().AsTask());
        ex.Identity.ShouldBe(_identity);
        ex.StateName.ShouldBe("balance");
    }

    [Fact]
    public async Task Should_Delete_State_And_Flush_Deletion_To_StateStore()
    {
        // Arrange
        var key = "actors:AccountActor:acc-101:temp";
        _stateStore.GetAsync<string>(_storeName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<string>("acc-101", "to-delete", "etag-del"));

        _stateStore.TryDeleteAsync(_storeName, key, "etag-del", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);

        // Act
        var val = await stateManager.GetStateAsync<string>("temp");
        val.ShouldBe("to-delete");

        var removed = await stateManager.RemoveStateAsync("temp");
        removed.ShouldBeTrue();

        var contains = await stateManager.ContainsStateAsync("temp");
        contains.ShouldBeFalse();

        await stateManager.SaveStateAsync();

        // Assert
        await _stateStore.Received(1).TryDeleteAsync(
            _storeName,
            key,
            "etag-del",
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_Store_Writes_When_State_Is_Unmodified()
    {
        // Arrange
        var key = "actors:AccountActor:acc-101:unchanged";
        _stateStore.GetAsync<string>(_storeName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<string>("acc-101", "unchanged-value", "etag-fixed"));

        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);

        // Act
        var val = await stateManager.GetStateAsync<string>("unchanged");
        val.ShouldBe("unchanged-value");

        await stateManager.SaveStateAsync();

        // Assert: zero writes or deletes performed!
        await _stateStore.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());

        await _stateStore.DidNotReceive().TrySetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Clear_Cache_Properly()
    {
        // Arrange
        var stateManager = new ActorStateManager(_identity, _stateStore, _storeName);
        await stateManager.SetStateAsync("cached", "data");

        var containsBefore = await stateManager.ContainsStateAsync("cached");
        containsBefore.ShouldBeTrue();

        await stateManager.ClearCacheAsync();

        var containsAfter = await stateManager.ContainsStateAsync("cached");
        containsAfter.ShouldBeFalse();
    }
}
