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

    private static bool HasTtlArg(IDictionary<string, object?>? arguments, long expectedTtl) =>
        arguments is not null && arguments.TryGetValue("x-message-ttl", out var value) && Equals(value, expectedTtl);

    private static bool HasCustomArgs(IDictionary<string, object?>? arguments, string expectedType, int expectedMax) =>
        arguments is not null &&
        arguments.TryGetValue("x-queue-type", out var type) && Equals(type, expectedType) &&
        arguments.TryGetValue("x-max-length", out var max) && Equals(max, expectedMax);

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

    [Fact]
    public async Task SubscribeAsync_With_PrefetchCount_Should_Call_BasicQosAsync()
    {
        var options = new PubSubSubscribeOptions { PrefetchCount = 25 };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.Received(1).BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 25,
            global: false,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_Without_Options_Should_Call_BasicQosAsync_With_DefaultPrefetchCount_50()
    {
        await _sut.SubscribeAsync("pubsub", "orders.created", (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success));

        await _channel.Received(1).BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 50,
            global: false,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_With_PrefetchCount_Zero_And_Zero_Default_Should_Not_Call_BasicQosAsync()
    {
        _options.DefaultPrefetchCount = 0;
        var options = new PubSubSubscribeOptions { PrefetchCount = 0 };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.DidNotReceive().BasicQosAsync(
            Arg.Any<uint>(),
            Arg.Any<ushort>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_With_AutoDelete_True_Should_Declare_NonDurable_AutoDelete_Queue()
    {
        var options = new PubSubSubscribeOptions { AutoDelete = true };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.Received(1).QueueDeclareAsync(
            queue: Arg.Any<string>(),
            durable: false,
            exclusive: false,
            autoDelete: true,
            arguments: Arg.Any<IDictionary<string, object?>?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_With_MessageTimeToLive_Should_Set_Ttl_Argument()
    {
        var options = new PubSubSubscribeOptions { MessageTimeToLive = TimeSpan.FromSeconds(30) };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.Received(1).QueueDeclareAsync(
            queue: Arg.Any<string>(),
            durable: Arg.Any<bool>(),
            exclusive: Arg.Any<bool>(),
            autoDelete: Arg.Any<bool>(),
            arguments: Arg.Is<IDictionary<string, object?>?>(a => HasTtlArg(a, 30000L)),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_With_CustomArguments_Should_Merge_Into_Queue_Arguments()
    {
        var options = new PubSubSubscribeOptions
        {
            CustomArguments = new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-max-length"] = 1000
            }
        };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.Received(1).QueueDeclareAsync(
            queue: Arg.Any<string>(),
            durable: Arg.Any<bool>(),
            exclusive: Arg.Any<bool>(),
            autoDelete: Arg.Any<bool>(),
            arguments: Arg.Is<IDictionary<string, object?>?>(a => HasCustomArgs(a, "quorum", 1000)),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_With_MaxConcurrentCalls_Greater_Than_1_Should_Attach_Consumer()
    {
        var options = new PubSubSubscribeOptions { MaxConcurrentCalls = 4 };

        await _sut.SubscribeAsync(
            "pubsub", "orders.created",
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
            options: options);

        await _channel.Received(1).BasicConsumeAsync(
            queue: Arg.Any<string>(),
            autoAck: false,
            consumer: Arg.Any<IAsyncBasicConsumer>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
