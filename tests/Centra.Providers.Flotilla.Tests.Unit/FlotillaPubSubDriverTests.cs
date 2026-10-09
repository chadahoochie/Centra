using System.Text;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.PubSub;
using Centra.PubSub;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaPubSubDriverTests
{
    private readonly IFlotillaClient _client = Substitute.For<IFlotillaClient>();

    [Fact]
    public async Task PublishAsync_SubmitsEncodedProposalToClient()
    {
        var driver = new FlotillaPubSubDriver(_client);
        var topic = "inventory.reserved";
        var payload = Encoding.UTF8.GetBytes("item-42");
        var metadata = new Dictionary<string, string> { ["traceId"] = "trace-101" };

        _client.ProposeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<FlotillaProposalResult>(FlotillaProposalResult.Success(100)));

        await driver.PublishAsync("default", topic, payload, metadata);

        await _client.Received(1).ProposeAsync(
            Arg.Is<ReadOnlyMemory<byte>>(mem =>
                VerifyEncodedPayload(mem, topic, "item-42")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenProposalFails_ThrowsInvalidOperationException()
    {
        var driver = new FlotillaPubSubDriver(_client);
        _client.ProposeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<FlotillaProposalResult>(FlotillaProposalResult.Failure("Raft quorum timeout")));

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await driver.PublishAsync("default", "orders.new", new byte[] { 1, 2, 3 }, new Dictionary<string, string>());
        });

        ex.Message.ShouldContain("Raft quorum timeout");
    }

    [Fact]
    public async Task PublishBatchAsync_SubmitsAllMessagesSequentially()
    {
        var driver = new FlotillaPubSubDriver(_client);
        _client.ProposeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<FlotillaProposalResult>(FlotillaProposalResult.Success(1)));

        var messages = new List<PubSubMessage>
        {
            new(Encoding.UTF8.GetBytes("msg1"), new Dictionary<string, string>()),
            new(Encoding.UTF8.GetBytes("msg2"), new Dictionary<string, string>()),
            new(Encoding.UTF8.GetBytes("msg3"), new Dictionary<string, string>()),
        };

        await driver.PublishBatchAsync("default", "batch.topic", messages);

        await _client.Received(3).ProposeAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_RegistersHandler_And_UnsubscribeAsync_RemovesIt()
    {
        var driver = new FlotillaPubSubDriver(_client);
        var topic = "alerts.critical";

        driver.GetSubscriptions(topic).ShouldBeEmpty();

        await driver.SubscribeAsync(
            "default",
            topic,
            (_, _, _) => ValueTask.FromResult(EventHandlingResult.Success));

        driver.GetSubscriptions(topic).Count.ShouldBe(1);

        await driver.UnsubscribeAsync("default", topic);

        driver.GetSubscriptions(topic).ShouldBeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_WhenRedeliveryBudgetConfigured_ThrowsNotSupportedException()
    {
        var driver = new FlotillaPubSubDriver(_client);
        var options = new PubSubSubscribeOptions { MaxRetryAttempts = 3 };

        await Should.ThrowAsync<NotSupportedException>(async () =>
        {
            await driver.SubscribeAsync(
                "default",
                "orders.topic",
                (_, _, _) => ValueTask.FromResult(EventHandlingResult.Success),
                options: options);
        });
    }

    [Fact]
    public void BeginShutdownDrain_ReturnsDisposableHandle()
    {
        var driver = new FlotillaPubSubDriver(_client);
        using var drain = driver.BeginShutdownDrain();
        drain.ShouldNotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpSubscriptionsAndDisposesClient()
    {
        var driver = new FlotillaPubSubDriver(_client);
        await driver.SubscribeAsync("default", "topic", (_, _, _) => ValueTask.FromResult(EventHandlingResult.Success));

        await driver.DisposeAsync();

        driver.GetSubscriptions("topic").ShouldBeEmpty();
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task SubscriptionWorker_DispatchesCommittedEntriesToMatchingHandlers()
    {
        var driver = new FlotillaPubSubDriver(_client);
        var topic = "events.dispatched";
        var payloadBytes = Encoding.UTF8.GetBytes("dispatched_data");
        var metadata = new Dictionary<string, string> { ["source"] = "test" };

        var encoded = FlotillaWireProtocol.EncodeMessage(topic, metadata, payloadBytes);

        var receivedList = new List<string>();
        var handlerInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await driver.SubscribeAsync("default", topic, (payload, meta, _) =>
        {
            receivedList.Add(Encoding.UTF8.GetString(payload.Span));
            handlerInvoked.TrySetResult(true);
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        // Set up mock stream of commits
        async IAsyncEnumerable<CommittedEntry> StreamCommits()
        {
            yield return new CommittedEntry
            {
                LogIndex = 1,
                Term = 1,
                Data = encoded,
            };
            await Task.Yield();
        }

        _client.SubscribeCommitsAsync(Arg.Any<CancellationToken>()).Returns(StreamCommits());

        var worker = new FlotillaSubscriptionWorker(_client, driver);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cts.Token);
        await handlerInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        receivedList.Count.ShouldBe(1);
        receivedList[0].ShouldBe("dispatched_data");
    }

    internal static bool VerifyEncodedPayload(ReadOnlyMemory<byte> memory, string expectedTopic, string expectedSubstring)
    {
        var (topic, _, payload) = FlotillaWireProtocol.DecodeMessage(memory);
        return topic == expectedTopic && Encoding.UTF8.GetString(payload.Span).Contains(expectedSubstring);
    }
}
