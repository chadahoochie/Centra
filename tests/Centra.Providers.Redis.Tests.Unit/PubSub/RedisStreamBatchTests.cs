using System.Text;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.PubSub;
using Centra.PubSub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.PubSub;

public sealed class RedisStreamBatchTests
{
    [Fact]
    public async Task PublishBatchToStreamAsync_UsesBatchPipelining()
    {
        // Arrange
        var db = Substitute.For<IDatabase>();
        var batch = Substitute.For<IBatch>();
        db.CreateBatch().Returns(batch);

        var messages = new[]
        {
            new PubSubMessage(Encoding.UTF8.GetBytes("msg-1"), new Dictionary<string, string> { ["k1"] = "v1" }),
            new PubSubMessage(Encoding.UTF8.GetBytes("msg-2"), new Dictionary<string, string> { ["k2"] = "v2" })
        };

        // Act
        await RedisStreamProcessor.Instance.PublishBatchToStreamAsync(
            db, "stream-key", messages, CancellationToken.None);

        // Assert
        db.Received(1).CreateBatch();
        await batch.Received(2).StreamAddAsync(
            (RedisKey)"stream-key",
            (RedisValue)"envelope",
            Arg.Any<RedisValue>(),
            null,
            null,
            false,
            CommandFlags.None);
        batch.Received(1).Execute();
    }

    [Fact]
    public async Task RunStreamLoopAsync_AcknowledgesBatchInSingleCall()
    {
        // Arrange
        var db = Substitute.For<IDatabase>();
        using var cts = new CancellationTokenSource();

        var entry1 = new StreamEntry("1-0", [new NameValueEntry("envelope", (RedisValue)RedisMessagePayloadCodec.Encode(Encoding.UTF8.GetBytes("d1"), null))]);
        var entry2 = new StreamEntry("2-0", [new NameValueEntry("envelope", (RedisValue)RedisMessagePayloadCodec.Encode(Encoding.UTF8.GetBytes("d2"), null))]);

        db.StreamReadGroupAsync(
            (RedisKey)"stream-1",
            (RedisValue)"grp-1",
            (RedisValue)"cons-1",
            StreamPosition.NewMessages,
            10,
            false,
            CommandFlags.None)
            .Returns(Task.FromResult(new[] { entry1, entry2 }));

        var processedCount = 0;

        // Act
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
                Interlocked.Increment(ref processedCount);
                if (processedCount == 2)
                {
                    cts.Cancel();
                }
                return ValueTask.FromResult(EventHandlingResult.Success);
            },
            ConsumerMode.CompetingConsumer,
            NullLogger.Instance,
            cts.Token);

        await loopTask;

        // Assert
        processedCount.ShouldBe(2);
        await db.Received(1).StreamAcknowledgeAsync(
            (RedisKey)"stream-1",
            (RedisValue)"grp-1",
            Arg.Is<RedisValue[]>(ids => ids.Length == 2 && ids[0] == "1-0" && ids[1] == "2-0"),
            CommandFlags.None);
    }

    [Fact]
    public async Task RedisPubSubDriver_PublishBatchAsync_CallsStreamProcessor_WhenConsumerGroupsEnabled()
    {
        // Arrange
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var streamProcessor = Substitute.For<IRedisStreamProcessor>();
        var options = new RedisProviderOptions { EnableConsumerGroups = true, KeyPrefix = "c:" };
        var sut = new RedisPubSubDriver(multiplexer, Microsoft.Extensions.Options.Options.Create(options), streamProcessor: streamProcessor);

        var messages = new[]
        {
            new PubSubMessage(Encoding.UTF8.GetBytes("m1")),
            new PubSubMessage(Encoding.UTF8.GetBytes("m2"))
        };

        // Act
        await sut.PublishBatchAsync("pubsub", "orders.created", messages);

        // Assert
        await streamProcessor.Received(1).PublishBatchToStreamAsync(
            db,
            "c:pubsub-stream:pubsub:orders.created",
            messages,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessStreamEntryAsync_WhenDeadLetter_WithoutEnvelopeField_PublishesSuccessfully()
    {
        // Arrange: entry with legacy payload and headers fields, NO envelope field
        var db = Substitute.For<IDatabase>();
        var payloadBytes = Encoding.UTF8.GetBytes("legacy-payload-dlq");
        var headersBytes = Encoding.UTF8.GetBytes("{\"ce-type\":\"dlq.test\"}");

        var entry = new StreamEntry("entry-99", [
            new NameValueEntry("payload", (RedisValue)payloadBytes),
            new NameValueEntry("headers", (RedisValue)headersBytes)
        ]);

        // Act: should not throw Sequence contains no matching element
        await RedisStreamProcessor.Instance.ProcessStreamEntryAsync(
            db,
            streamKey: "test-stream",
            groupName: "test-grp",
            pubSubName: "my-pubsub",
            deadLetterTopic: "dlq-topic",
            handler: (p, h, ct) => ValueTask.FromResult(EventHandlingResult.DeadLetter),
            entry: entry,
            logger: NullLogger.Instance,
            cancellationToken: CancellationToken.None,
            autoAcknowledge: true);

        // Assert: StreamAddAsync was called on the dead letter stream
        await db.Received(1).StreamAddAsync(
            (RedisKey)"dlq-topic",
            (RedisValue)"envelope",
            Arg.Any<RedisValue>(),
            null,
            null,
            false,
            CommandFlags.None);
    }

    [Fact]
    public async Task RunStreamLoopAsync_WhenCancelledMidBatch_AcknowledgesOnlyProcessedEntries()
    {
        // Arrange
        var db = Substitute.For<IDatabase>();
        using var cts = new CancellationTokenSource();

        var entry1 = new StreamEntry("1-0", [new NameValueEntry("envelope", (RedisValue)RedisMessagePayloadCodec.Encode(Encoding.UTF8.GetBytes("d1"), null))]);
        var entry2 = new StreamEntry("2-0", [new NameValueEntry("envelope", (RedisValue)RedisMessagePayloadCodec.Encode(Encoding.UTF8.GetBytes("d2"), null))]);

        db.StreamReadGroupAsync(
            (RedisKey)"stream-1",
            (RedisValue)"grp-1",
            (RedisValue)"cons-1",
            StreamPosition.NewMessages,
            10,
            false,
            CommandFlags.None)
            .Returns(Task.FromResult(new[] { entry1, entry2 }));

        var processedCount = 0;

        // Act: entry 1 succeeds, entry 2 throws OperationCanceledException
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
                var count = Interlocked.Increment(ref processedCount);
                if (count == 1)
                {
                    return ValueTask.FromResult(EventHandlingResult.Success);
                }

                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
            ConsumerMode.CompetingConsumer,
            NullLogger.Instance,
            cts.Token);

        await loopTask;

        // Assert: only entry 1-0 should have been acknowledged, NOT entry 2-0
        await db.Received(1).StreamAcknowledgeAsync(
            (RedisKey)"stream-1",
            (RedisValue)"grp-1",
            Arg.Is<RedisValue[]>(ids => ids.Length == 1 && ids[0] == "1-0"),
            CommandFlags.None);
    }
}
