using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class RabbitMQPubSubDriverTests
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly RabbitMQProviderOptions _options;
    private readonly RabbitMQPubSubDriver _sut;

    public RabbitMQPubSubDriverTests()
    {
        _connectionFactory = Substitute.For<IConnectionFactory>();
        _connection = Substitute.For<IConnection>();
        _channel = Substitute.For<IChannel>();

        _connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(_connection);
        _connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(_channel);
        _channel.BasicConsumeAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<IDictionary<string, object?>>(), Arg.Any<IAsyncBasicConsumer>(), Arg.Any<CancellationToken>())
            .Returns("consumer-tag");

        _options = new RabbitMQProviderOptions();
        _sut = new RabbitMQPubSubDriver(_connectionFactory, Microsoft.Extensions.Options.Options.Create(_options));
    }

    private static bool HasSingleActiveConsumerArg(IDictionary<string, object?>? arguments) =>
        arguments is not null && arguments.TryGetValue("x-single-active-consumer", out var value) && value is true;

    [Fact]
    public async Task SubscribeAsync_With_CompetingConsumer_Should_Not_Set_SingleActiveConsumer_Arg()
    {
        await _sut.SubscribeAsync("pubsub", "orders.created", (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success));

        await _channel.Received(1).QueueDeclareAsync(
            queue: Arg.Any<string>(),
            durable: Arg.Any<bool>(),
            exclusive: Arg.Any<bool>(),
            autoDelete: Arg.Any<bool>(),
            arguments: Arg.Is<IDictionary<string, object?>?>(a => a == null || !a.ContainsKey("x-single-active-consumer")),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_With_SingleActiveConsumer_Should_Set_SingleActiveConsumer_Arg()
    {
        var options = new PubSubSubscribeOptions { ConsumerMode = ConsumerMode.SingleActiveConsumer };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.Received(1).QueueDeclareAsync(
            queue: Arg.Any<string>(),
            durable: Arg.Any<bool>(),
            exclusive: Arg.Any<bool>(),
            autoDelete: Arg.Any<bool>(),
            arguments: Arg.Is<IDictionary<string, object?>?>(a => HasSingleActiveConsumerArg(a)),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
