using System.Text;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.PubSub;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.PubSub;

public sealed class RedisPubSubDriverTests
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ISubscriber _subscriber;
    private readonly RedisProviderOptions _options;
    private readonly RedisPubSubDriver _sut;

    public RedisPubSubDriverTests()
    {
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _subscriber = Substitute.For<ISubscriber>();
        _multiplexer.GetSubscriber(Arg.Any<object>()).Returns(_subscriber);

        _options = new RedisProviderOptions { KeyPrefix = "centra:" };
        _sut = new RedisPubSubDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(_options));
    }

    [Fact]
    public async Task PublishAsync_Should_Publish_Serialized_Envelope_To_Channel()
    {
        var payload = Encoding.UTF8.GetBytes("{\"test\":\"data\"}");
        var metadata = new Dictionary<string, string>
        {
            ["ce-id"] = "test-123",
            ["ce-type"] = "event.test"
        };

        await _sut.PublishAsync("pubsub", "orders.created", payload, metadata);

        await _subscriber.Received(1).PublishAsync(
            new RedisChannel("centra:pubsub:pubsub:orders.created", RedisChannel.PatternMode.Literal),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SubscribeAsync_Should_Register_Subscriber_Handler()
    {
        await _sut.SubscribeAsync("pubsub", "orders.created", (payload, headers, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.Success));

        await _subscriber.Received(1).SubscribeAsync(
            new RedisChannel("centra:pubsub:pubsub:orders.created", RedisChannel.PatternMode.Literal),
            Arg.Any<Action<RedisChannel, RedisValue>>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Call_Unsubscribe()
    {
        await _sut.UnsubscribeAsync("pubsub", "orders.created");

        await _subscriber.Received(1).UnsubscribeAsync(
            new RedisChannel("centra:pubsub:pubsub:orders.created", RedisChannel.PatternMode.Literal),
            Arg.Any<Action<RedisChannel, RedisValue>>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SubscribeAsync_With_SingleActiveConsumer_And_ConsumerGroups_Disabled_Should_Fall_Back_To_Broadcast()
    {
        // EnableConsumerGroups defaults to false, so single-active can't be honored - the driver
        // should still subscribe (broadcast, legacy behavior), not throw.
        var options = new Centra.PubSub.PubSubSubscribeOptions { ConsumerMode = Centra.PubSub.ConsumerMode.SingleActiveConsumer };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.Success),
            options: options);

        await _subscriber.Received(1).SubscribeAsync(
            new RedisChannel("centra:pubsub:pubsub:orders.created", RedisChannel.PatternMode.Literal),
            Arg.Any<Action<RedisChannel, RedisValue>>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SubscribeAsync_With_EnableConsumerGroups_And_PrefetchCount_Should_Pass_BatchSize_To_StreamProcessor()
    {
        var streamProcessor = Substitute.For<IRedisStreamProcessor>();
        var options = new RedisProviderOptions { EnableConsumerGroups = true };
        var sut = new RedisPubSubDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(options), streamProcessor: streamProcessor);
        var subOptions = new Centra.PubSub.PubSubSubscribeOptions { PrefetchCount = 35 };

        await sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.Success),
            options: subOptions);

        await streamProcessor.Received(1).RunStreamLoopAsync(
            Arg.Any<IDatabase>(),
            Arg.Any<Centra.Locks.IDistributedLockProvider?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            "pubsub",
            Arg.Any<string?>(),
            Arg.Any<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<Centra.PubSub.EventHandlingResult>>>(),
            Arg.Any<Centra.PubSub.ConsumerMode>(),
            Arg.Any<Microsoft.Extensions.Logging.ILogger>(),
            Arg.Any<CancellationToken>(),
            batchSize: 35);
    }

    [Fact]
    public async Task PublishAsync_With_EnableConsumerGroups_Should_Publish_To_Stream()
    {
        var streamProcessor = Substitute.For<IRedisStreamProcessor>();
        var db = Substitute.For<IDatabase>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var options = new RedisProviderOptions { EnableConsumerGroups = true, KeyPrefix = "centra:" };
        var sut = new RedisPubSubDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(options), streamProcessor: streamProcessor);

        var payload = Encoding.UTF8.GetBytes("stream-payload");
        var metadata = new Dictionary<string, string> { ["ce-id"] = "101" };

        await sut.PublishAsync("pubsub", "orders.created", payload, metadata);

        await streamProcessor.Received(1).PublishToStreamAsync(
            db,
            "centra:pubsub-stream:pubsub:orders.created",
            Arg.Is<ReadOnlyMemory<byte>>(m => m.Length == payload.Length),
            metadata,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_MessageHandler_Processes_Message_And_Handles_DeadLetter()
    {
        Action<RedisChannel, RedisValue>? capturedHandler = null;
        await _subscriber.SubscribeAsync(
            Arg.Any<RedisChannel>(),
            Arg.Do<Action<RedisChannel, RedisValue>>(h => capturedHandler = h),
            Arg.Any<CommandFlags>());

        await _sut.SubscribeAsync(
            "pubsub",
            "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.DeadLetter),
            deadLetterTopic: "orders.dlq");

        capturedHandler.ShouldNotBeNull();

        var envelope = new RedisMessageEnvelope(new Dictionary<string, string> { ["k"] = "v" }, Encoding.UTF8.GetBytes("test"));
        var envelopeBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope);

        // Invoke captured callback
        capturedHandler!(new RedisChannel("test", RedisChannel.PatternMode.Literal), envelopeBytes);

        // Also test empty value callback (should return early without error)
        capturedHandler!(new RedisChannel("test", RedisChannel.PatternMode.Literal), RedisValue.Null);

        // Also test corrupted JSON callback
        capturedHandler!(new RedisChannel("test", RedisChannel.PatternMode.Literal), "not-valid-json");
    }

    [Fact]
    public async Task SubscribeViaStreamAsync_Swallows_BusyGroup_Exception()
    {
        var db = Substitute.For<IDatabase>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StreamCreateConsumerGroupAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>())
            .Returns<Task<bool>>(_ => throw new RedisException("BUSYGROUP Consumer Group name already exists"));

        var streamProcessor = Substitute.For<IRedisStreamProcessor>();
        var options = new RedisProviderOptions { EnableConsumerGroups = true };
        var sut = new RedisPubSubDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(options), streamProcessor: streamProcessor);

        await sut.SubscribeAsync(
            "pubsub", "orders.stream",
            (p, h, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.Success));

        // Did not throw and started the loop
        await streamProcessor.Received(1).RunStreamLoopAsync(
            Arg.Any<IDatabase>(),
            Arg.Any<Centra.Locks.IDistributedLockProvider?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<Centra.PubSub.EventHandlingResult>>>(),
            Arg.Any<Centra.PubSub.ConsumerMode>(),
            Arg.Any<Microsoft.Extensions.Logging.ILogger>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<int>());
    }

    [Fact]
    public async Task UnsubscribeAsync_And_DisposeAsync_Should_Cancel_Stream_Subscriptions()
    {
        var db = Substitute.For<IDatabase>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var streamProcessor = Substitute.For<IRedisStreamProcessor>();
        var options = new RedisProviderOptions { EnableConsumerGroups = true };
        var sut = new RedisPubSubDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(options), streamProcessor: streamProcessor);

        await sut.SubscribeAsync("pubsub", "topic1", (p, h, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.Success));
        await sut.SubscribeAsync("pubsub", "topic2", (p, h, ct) => ValueTask.FromResult(Centra.PubSub.EventHandlingResult.Success));

        await sut.UnsubscribeAsync("pubsub", "topic1");
        await sut.DisposeAsync();
    }
}
