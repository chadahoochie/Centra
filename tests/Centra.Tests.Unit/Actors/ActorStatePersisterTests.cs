using Centra.Actors;
using Centra.Core.Actors;
using Centra.State;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorStatePersisterTests
{
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly ActorIdentity _identity = new("CustomerActor", "c-100");

    [Fact]
    public async Task PersistEntryAsync_Should_Validate_Key_And_StateName()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(new byte[] { 1 }, "etag-1", ActorStateStatus.Added, typeof(byte[]));

        await Should.ThrowAsync<ArgumentException>(() =>
            persister.PersistEntryAsync(_identity, "store", "", "state", entry, CancellationToken.None).AsTask());

        await Should.ThrowAsync<ArgumentException>(() =>
            persister.PersistEntryAsync(_identity, "store", "key", "   ", entry, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task PersistEntryAsync_Added_Should_Set_And_Refresh_ETag()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(new byte[] { 1, 2, 3 }, null, ActorStateStatus.Added, typeof(byte[]));

        _stateStore.GetAsync<object>("store", "key", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<StateEntry<object>?>(new StateEntry<object>("key", new object(), "new-etag")));

        await persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None);

        await _stateStore.Received(1).SetAsync("store", "key", entry.Value!, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
        entry.ETag.ShouldBe("new-etag");
        entry.Status.ShouldBe(ActorStateStatus.Unchanged);
    }

    [Fact]
    public async Task PersistEntryAsync_Modified_With_Empty_ETag_Should_Blind_Set()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(new byte[] { 4, 5, 6 }, "", ActorStateStatus.Modified, typeof(byte[]));

        _stateStore.GetAsync<object>("store", "key", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<StateEntry<object>?>(new StateEntry<object>("key", new object(), "etag-2")));

        await persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None);

        await _stateStore.Received(1).SetAsync("store", "key", entry.Value!, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
        entry.ETag.ShouldBe("etag-2");
        entry.Status.ShouldBe(ActorStateStatus.Unchanged);
    }

    [Fact]
    public async Task PersistEntryAsync_Modified_Conflict_Should_Throw_ActorConcurrencyException()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(new byte[] { 4, 5, 6 }, "etag-old", ActorStateStatus.Modified, typeof(byte[]));

        _stateStore.TrySetAsync("store", "key", entry.Value!, "etag-old", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var ex = await Should.ThrowAsync<ActorConcurrencyException>(() =>
            persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None).AsTask());

        ex.Identity.ShouldBe(_identity);
        ex.StateName.ShouldBe("state");
    }

    [Fact]
    public async Task PersistEntryAsync_Modified_Success_Should_Update_ETag()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(new byte[] { 4, 5, 6 }, "etag-old", ActorStateStatus.Modified, typeof(byte[]));

        _stateStore.TrySetAsync("store", "key", entry.Value!, "etag-old", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        _stateStore.GetAsync<object>("store", "key", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<StateEntry<object>?>(new StateEntry<object>("key", new object(), "etag-new")));

        await persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None);

        entry.ETag.ShouldBe("etag-new");
        entry.Status.ShouldBe(ActorStateStatus.Unchanged);
    }

    [Fact]
    public async Task PersistEntryAsync_Deleted_With_Empty_ETag_Should_Blind_Delete()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(null, "", ActorStateStatus.Deleted, typeof(byte[]));

        await persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None);

        await _stateStore.Received(1).DeleteAsync("store", "key", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistEntryAsync_Deleted_Conflict_Should_Throw_ActorConcurrencyException()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(null, "etag-1", ActorStateStatus.Deleted, typeof(byte[]));

        _stateStore.TryDeleteAsync("store", "key", "etag-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var ex = await Should.ThrowAsync<ActorConcurrencyException>(() =>
            persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None).AsTask());

        ex.Identity.ShouldBe(_identity);
        ex.StateName.ShouldBe("state");
    }

    [Fact]
    public async Task PersistEntryAsync_Deleted_Success_Should_Complete()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(null, "etag-1", ActorStateStatus.Deleted, typeof(byte[]));

        _stateStore.TryDeleteAsync("store", "key", "etag-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        await persister.PersistEntryAsync(_identity, "store", "key", "state", entry, CancellationToken.None);
    }

    [Fact]
    public async Task RefreshETagAsync_When_Not_Found_Should_Not_Change_ETag()
    {
        var persister = new ActorStatePersister(_stateStore);
        var entry = new ActorStateEntry(new byte[] { 1 }, "initial-etag", ActorStateStatus.Unchanged, typeof(byte[]));

        _stateStore.GetAsync<object>("store", "key", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<StateEntry<object>?>(null));

        await persister.RefreshETagAsync("store", "key", entry, CancellationToken.None);

        entry.ETag.ShouldBe("initial-etag");
    }
}
