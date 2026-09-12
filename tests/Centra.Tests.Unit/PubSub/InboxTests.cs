using Centra.Hosting.Inbox;
using Centra.PubSub.Inbox;
using Centra.State;
using Centra.Tests.Unit.Common;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class InboxTests
{
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();

    [Theory, AutoNSubstituteData]
    public async Task Should_Detect_Duplicate_Event_In_InMemoryInboxStore(
        string messageId,
        string consumerId)
    {
        var inbox = new InMemoryInboxStore();

        var before = await inbox.HasBeenProcessedAsync(messageId, consumerId);
        before.ShouldBeFalse();

        await inbox.MarkProcessedAsync(messageId, consumerId);

        var after = await inbox.HasBeenProcessedAsync(messageId, consumerId);
        after.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Expire_Deduplication_Key_After_Retention_Period()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var inbox = new InMemoryInboxStore(timeProvider);

        await inbox.MarkProcessedAsync("msg-1", "consumer-1", TimeSpan.FromMinutes(5));

        (await inbox.HasBeenProcessedAsync("msg-1", "consumer-1")).ShouldBeTrue();

        timeProvider.Advance(TimeSpan.FromMinutes(6));

        (await inbox.HasBeenProcessedAsync("msg-1", "consumer-1")).ShouldBeFalse();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Deduplicate_In_StateStoreInboxStore(
        string messageId,
        string consumerId)
    {
        var key = $"centra:inbox:{consumerId}:{messageId}";
        _stateStore.GetAsync<bool>("statestore", key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new StateEntry<bool>(key, true, "etag-1"));

        var inbox = new StateStoreInboxStore(_stateStore, "statestore");
        var isProcessed = await inbox.HasBeenProcessedAsync(messageId, consumerId);

        isProcessed.ShouldBeTrue();
    }
}
