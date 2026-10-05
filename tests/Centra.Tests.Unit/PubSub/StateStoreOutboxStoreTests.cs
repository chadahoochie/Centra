using Centra.Hosting.Outbox;
using Centra.PubSub.Outbox;
using Centra.State;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class StateStoreOutboxStoreTests
{
    [Fact]
    public void Constructor_Should_Throw_When_StateStore_Is_Null()
    {
        Should.Throw<ArgumentNullException>(() => new StateStoreOutboxStore(null!));
    }

    [Fact]
    public async Task EnqueueAsync_Should_Throw_When_Message_Is_Null()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore);

        await Should.ThrowAsync<ArgumentNullException>(async () => await sut.EnqueueAsync(null!));
    }

    [Fact]
    public async Task EnqueueAsync_Should_Set_Message_And_Update_Pending_Index()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        var msg = new OutboxMessage(
            "msg-1",
            "pubsub-1",
            "orders",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string> { ["k"] = "v" },
            DateTimeOffset.UtcNow);

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<List<string>>("centra:outbox:pending", new List<string>(), "etag-1"));

        stateStore.TrySetAsync("mystore", "centra:outbox:pending", Arg.Any<List<string>>(), "etag-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await sut.EnqueueAsync(msg);

        await stateStore.Received(1).SetAsync(
            "mystore",
            "centra:outbox:msg:msg-1",
            Arg.Is<OutboxMessageRecord>(r => r.Id == "msg-1" && r.Topic == "orders"),
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());

        await stateStore.Received(1).TrySetAsync(
            "mystore",
            "centra:outbox:pending",
            Arg.Is<List<string>>(l => l.Contains("msg-1")),
            "etag-1",
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueAsync_Should_Retry_When_Concurrency_Conflict_Occurs()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        var msg = new OutboxMessage("msg-conflict", "pubsub-1", "orders", new byte[] { 1 }, new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(
                new StateEntry<List<string>>("centra:outbox:pending", new List<string>(), "etag-1"),
                new StateEntry<List<string>>("centra:outbox:pending", new List<string> { "other" }, "etag-2"));

        stateStore.TrySetAsync("mystore", "centra:outbox:pending", Arg.Any<List<string>>(), "etag-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        stateStore.TrySetAsync("mystore", "centra:outbox:pending", Arg.Any<List<string>>(), "etag-2", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await sut.EnqueueAsync(msg);

        await stateStore.Received(2).TrySetAsync("mystore", "centra:outbox:pending", Arg.Any<List<string>>(), Arg.Any<string>(), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Return_Empty_When_Index_Missing_Or_Empty()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns((StateEntry<List<string>>?)null);

        var result = await sut.FetchPendingAsync(10);
        result.ShouldBeEmpty();

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<List<string>>("centra:outbox:pending", new List<string>(), "etag-0"));

        var resultEmpty = await sut.FetchPendingAsync(10);
        resultEmpty.ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Fallback_To_Individual_GetAsync_When_Batch_Returns_Empty()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<List<string>>("centra:outbox:pending", new List<string> { "msg-10" }, "etag-1"));

        stateStore.GetBatchAsync<OutboxMessageRecord>("mystore", Arg.Any<IReadOnlyList<string>>(), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new List<StateEntry<OutboxMessageRecord>>());

        var msgRecord = new OutboxMessageRecord("msg-10", "pubsub-1", "orders", new byte[] { 9 }, new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        stateStore.GetAsync<OutboxMessageRecord>("mystore", "centra:outbox:msg:msg-10", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<OutboxMessageRecord>("centra:outbox:msg:msg-10", msgRecord, "etag-msg"));

        var pending = await sut.FetchPendingAsync(10);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe("msg-10");
        pending[0].Topic.ShouldBe("orders");
    }

    [Fact]
    public async Task MarkPublishedAsync_Should_Throw_When_MessageId_Is_Null_Or_Whitespace()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore);

        await Should.ThrowAsync<ArgumentException>(async () => await sut.MarkPublishedAsync(null!));
        await Should.ThrowAsync<ArgumentException>(async () => await sut.MarkPublishedAsync(string.Empty));
        await Should.ThrowAsync<ArgumentException>(async () => await sut.MarkPublishedAsync("   "));
    }

    [Fact]
    public async Task MarkPublishedAsync_Should_Delete_Message_And_Remove_From_Pending()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<List<string>>("centra:outbox:pending", new List<string> { "msg-del", "msg-keep" }, "etag-del"));

        stateStore.TrySetAsync("mystore", "centra:outbox:pending", Arg.Any<List<string>>(), "etag-del", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await sut.MarkPublishedAsync("msg-del");

        await stateStore.Received(1).DeleteAsync("mystore", "centra:outbox:msg:msg-del", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
        await stateStore.Received(1).TrySetAsync(
            "mystore",
            "centra:outbox:pending",
            Arg.Is<List<string>>(l => !l.Contains("msg-del") && l.Contains("msg-keep")),
            "etag-del",
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPublishedAsync_Should_Exit_Early_When_Index_Missing_Or_Message_Not_Found()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        stateStore.GetAsync<List<string>>("mystore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns((StateEntry<List<string>>?)null);

        await sut.MarkPublishedAsync("msg-not-found");

        await stateStore.Received(1).DeleteAsync("mystore", "centra:outbox:msg:msg-not-found", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
        await stateStore.DidNotReceive().TrySetAsync("mystore", "centra:outbox:pending", Arg.Any<List<string>>(), Arg.Any<string>(), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkFailedAsync_Should_Throw_When_MessageId_Is_Null_Or_Whitespace()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore);

        await Should.ThrowAsync<ArgumentException>(async () => await sut.MarkFailedAsync(null!, "err"));
        await Should.ThrowAsync<ArgumentException>(async () => await sut.MarkFailedAsync(string.Empty, "err"));
        await Should.ThrowAsync<ArgumentException>(async () => await sut.MarkFailedAsync("  ", "err"));
    }

    [Fact]
    public async Task MarkFailedAsync_Should_Set_Error_State()
    {
        var stateStore = Substitute.For<IStateStore>();
        var sut = new StateStoreOutboxStore(stateStore, "mystore");

        await sut.MarkFailedAsync("msg-failed", "Broker error");

        await stateStore.Received(1).SetAsync("mystore", "centra:outbox:fail:msg-failed", "Broker error", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }
}
