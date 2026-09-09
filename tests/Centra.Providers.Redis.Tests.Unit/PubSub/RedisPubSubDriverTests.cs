using System.Text;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.PubSub;
using Microsoft.Extensions.Options;
using NSubstitute;
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
}
