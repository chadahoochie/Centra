using System.Text;
using System.Text.Json;
using Centra.Locks;
using Centra.Providers.Redis.PubSub;
using Centra.PubSub;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.PubSub;

public sealed class RedisStreamProcessorTests
{
    [Fact]
    public async Task PublishToStreamAsync_ThrowsArgumentNullException_WhenDbIsNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            RedisStreamProcessor.Instance.PublishToStreamAsync(
                null!, "stream-key", ReadOnlyMemory<byte>.Empty, new Dictionary<string, string>(), CancellationToken.None).AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishToStreamAsync_ThrowsArgumentException_WhenStreamKeyIsInvalid(string key)
    {
        var db = Substitute.For<IDatabase>();
        await Should.ThrowAsync<ArgumentException>(() =>
            RedisStreamProcessor.Instance.PublishToStreamAsync(
                db, key, ReadOnlyMemory<byte>.Empty, new Dictionary<string, string>(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task PublishToStreamAsync_SerializesEnvelopeAndAddsToStream()
    {
        var db = Substitute.For<IDatabase>();
        var payload = Encoding.UTF8.GetBytes("hello world");
        var metadata = new Dictionary<string, string> { ["ce-id"] = "123" };

        await RedisStreamProcessor.Instance.PublishToStreamAsync(
            db, "test-stream", payload, metadata, CancellationToken.None);

        await db.Received(1).StreamAddAsync(
            (RedisKey)"test-stream",
            (RedisValue)"envelope",
            Arg.Any<RedisValue>(),
            null,
            null,
            false,
            CommandFlags.None);
    }

    [Fact]
    public async Task ProcessStreamEntryAsync_ProcessesMessageAndAcknowledges()
    {
        var db = Substitute.For<IDatabase>();
        var payload = Encoding.UTF8.GetBytes("sample-data");
        var headers = new Dictionary<string, string> { ["header1"] = "val1" };
        var envelope = new RedisMessageEnvelope(headers, payload);
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var entry = new StreamEntry("152697227-0", [new NameValueEntry("envelope", (RedisValue)envelopeBytes)]);

        byte[]? receivedPayload = null;
        IReadOnlyDictionary<string, string>? receivedHeaders = null;

        await RedisStreamProcessor.Instance.ProcessStreamEntryAsync(
            db,
            "stream-key",
            "group-1",
            "pubsub-1",
            deadLetterTopic: null,
            (p, h, ct) =>
            {
                receivedPayload = p.ToArray();
                receivedHeaders = h;
                return ValueTask.FromResult(EventHandlingResult.Success);
            },
            entry,
            NullLogger.Instance,
            CancellationToken.None);

        receivedPayload.ShouldNotBeNull();
        receivedPayload.ShouldBe(payload);
        receivedHeaders.ShouldNotBeNull();
        receivedHeaders["header1"].ShouldBe("val1");

        await db.Received(1).StreamAcknowledgeAsync(
            (RedisKey)"stream-key",
            (RedisValue)"group-1",
            entry.Id,
            CommandFlags.None);
    }

    [Fact]
    public async Task ProcessStreamEntryAsync_PublishesToDeadLetterTopic_WhenResultIsDeadLetter()
    {
        var db = Substitute.For<IDatabase>();
        var payload = Encoding.UTF8.GetBytes("bad-data");
        var headers = new Dictionary<string, string> { ["reason"] = "corrupt" };
        var envelope = new RedisMessageEnvelope(headers, payload);
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var entry = new StreamEntry("152697228-0", [new NameValueEntry("envelope", (RedisValue)envelopeBytes)]);

        await RedisStreamProcessor.Instance.ProcessStreamEntryAsync(
            db,
            "stream-key",
            "group-1",
            "pubsub-1",
            deadLetterTopic: "stream-dlq",
            (p, h, ct) => ValueTask.FromResult(EventHandlingResult.DeadLetter),
            entry,
            NullLogger.Instance,
            CancellationToken.None);

        await db.Received(1).StreamAddAsync(
            (RedisKey)"stream-dlq",
            (RedisValue)"envelope",
            Arg.Any<RedisValue>(),
            null,
            null,
            false,
            CommandFlags.None);

        await db.Received(1).StreamAcknowledgeAsync(
            (RedisKey)"stream-key",
            (RedisValue)"group-1",
            entry.Id,
            CommandFlags.None);
    }

    [Fact]
    public async Task ProcessStreamEntryAsync_AcknowledgesEvenWhenHandlerThrows()
    {
        var db = Substitute.For<IDatabase>();
        var entry = new StreamEntry("152697229-0", [new NameValueEntry("envelope", (RedisValue)Array.Empty<byte>())]);

        await RedisStreamProcessor.Instance.ProcessStreamEntryAsync(
            db,
            "stream-key",
            "group-1",
            "pubsub-1",
            deadLetterTopic: null,
            (p, h, ct) => throw new InvalidOperationException("boom"),
            entry,
            NullLogger.Instance,
            CancellationToken.None);

        await db.Received(1).StreamAcknowledgeAsync(
            (RedisKey)"stream-key",
            (RedisValue)"group-1",
            entry.Id,
            CommandFlags.None);
    }

    [Fact]
    public async Task RunStreamLoopAsync_ProcessesEntriesAndExitsOnCancellation()
    {
        var db = Substitute.For<IDatabase>();
        using var cts = new CancellationTokenSource();

        var payload = Encoding.UTF8.GetBytes("test");
        var envelope = new RedisMessageEnvelope(new Dictionary<string, string>(), payload);
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var entry = new StreamEntry("100-0", [new NameValueEntry("envelope", (RedisValue)envelopeBytes)]);

        db.StreamReadGroupAsync(
            (RedisKey)"stream-1",
            (RedisValue)"grp-1",
            (RedisValue)"cons-1",
            StreamPosition.NewMessages,
            10,
            false,
            CommandFlags.None)
            .Returns(Task.FromResult(new[] { entry }));

        var processed = false;
        var loopTask = RedisStreamProcessor.Instance.RunStreamLoopAsync(
            db,
            lockProvider: null,
            streamKey: "stream-1",
            groupName: "grp-1",
            consumerName: "cons-1",
            pubSubName: "pubsub-1",
            deadLetterTopic: null,
            (p, h, ct) =>
            {
                processed = true;
                cts.Cancel();
                return ValueTask.FromResult(EventHandlingResult.Success);
            },
            ConsumerMode.CompetingConsumer,
            NullLogger.Instance,
            cts.Token);

        await loopTask;

        processed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunStreamLoopAsync_WithSingleActiveConsumer_AcquiresAndReleasesLock()
    {
        var db = Substitute.For<IDatabase>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var distLock = Substitute.For<IDistributedLock>();
        using var cts = new CancellationTokenSource();

        lockProvider.TryAcquireLockAsync(
            "pubsub-1-pubsub-lock",
            "stream-1:single-active",
            TimeSpan.FromSeconds(30),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IDistributedLock?>(distLock));

        db.StreamReadGroupAsync(
            (RedisKey)"stream-1",
            (RedisValue)"grp-1",
            (RedisValue)"cons-1",
            StreamPosition.NewMessages,
            10,
            false,
            CommandFlags.None)
            .Returns(Task.FromResult(Array.Empty<StreamEntry>()));

        var loopTask = RedisStreamProcessor.Instance.RunStreamLoopAsync(
            db,
            lockProvider,
            streamKey: "stream-1",
            groupName: "grp-1",
            consumerName: "cons-1",
            pubSubName: "pubsub-1",
            deadLetterTopic: null,
            (p, h, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            ConsumerMode.SingleActiveConsumer,
            NullLogger.Instance,
            cts.Token);

        // Cancel quickly so it completes
        await Task.Delay(50);
        cts.Cancel();
        await loopTask;

        await distLock.Received(1).DisposeAsync();
    }
}
