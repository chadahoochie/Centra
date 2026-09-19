using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

/// <summary>
/// Covers the subscription shutdown contract: cancel the consumer so no new deliveries arrive,
/// then await in-flight handlers up to a configurable timeout before the channel is closed.
/// </summary>
public sealed class RabbitMQPubSubDriverDrainTests
{
    private const string PubSubName = "pubsub";
    private const string Topic = "orders.created";
    private const string ConsumerTag = "consumer-tag";

    private readonly IChannel _channel;
    private readonly RabbitMQProviderOptions _options;
    private readonly RecordingLogger<RabbitMQPubSubDriver> _logger;
    private readonly RabbitMQPubSubDriver _sut;
    private IAsyncBasicConsumer? _consumer;

    public RabbitMQPubSubDriverDrainTests()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        _channel = Substitute.For<IChannel>();

        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(_channel);
        _channel.BasicConsumeAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<IDictionary<string, object?>>(),
            Arg.Do<IAsyncBasicConsumer>(c => _consumer = c),
            Arg.Any<CancellationToken>())
            .Returns(ConsumerTag);

        _options = new RabbitMQProviderOptions();
        _logger = new RecordingLogger<RabbitMQPubSubDriver>();
        _sut = new RabbitMQPubSubDriver(connectionFactory, Microsoft.Extensions.Options.Options.Create(_options), _logger);
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Await_InFlight_Handlers_Before_Closing_The_Channel()
    {
        const int k = 5;
        var completed = 0;
        var completedWhenChannelClosed = -1;
        _channel
            .When(c => c.CloseAsync(Arg.Any<ushort>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()))
            .Do(_ => completedWhenChannelClosed = Volatile.Read(ref completed));

        await _sut.SubscribeAsync(
            PubSubName,
            Topic,
            async (payload, headers, ct) =>
            {
                await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref completed);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { MaxConcurrentCalls = k * 2 });

        _consumer.ShouldNotBeNull();
        await new ConsumerDeliverySource(_consumer, ConsumerTag).DeliverAsync(k, _options.ExchangeName, Topic);

        await _sut.UnsubscribeAsync(PubSubName, Topic);

        Volatile.Read(ref completed).ShouldBe(k);
        completedWhenChannelClosed.ShouldBe(k);
        await _channel.Received(k).BasicAckAsync(Arg.Any<ulong>(), multiple: false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), requeue: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_Should_Cancel_The_Consumer_And_Await_InFlight_Handlers()
    {
        const int k = 4;
        var completed = 0;

        await _sut.SubscribeAsync(
            PubSubName,
            Topic,
            async (payload, headers, ct) =>
            {
                await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref completed);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { MaxConcurrentCalls = k * 2 });

        _consumer.ShouldNotBeNull();
        await new ConsumerDeliverySource(_consumer, ConsumerTag).DeliverAsync(k, _options.ExchangeName, Topic);

        await _sut.DisposeAsync();

        Volatile.Read(ref completed).ShouldBe(k);
        await _channel.Received(1).BasicCancelAsync(ConsumerTag, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _channel.Received(k).BasicAckAsync(Arg.Any<ulong>(), multiple: false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Report_When_The_Drain_Timeout_Is_Exceeded()
    {
        _options.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100);
        using var release = new SemaphoreSlim(0, 1);

        await _sut.SubscribeAsync(
            PubSubName,
            Topic,
            async (payload, headers, ct) =>
            {
                await release.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { MaxConcurrentCalls = 2 });

        _consumer.ShouldNotBeNull();
        await new ConsumerDeliverySource(_consumer, ConsumerTag).DeliverAsync(1, _options.ExchangeName, Topic);

        await _sut.UnsubscribeAsync(PubSubName, Topic);

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("drain", StringComparison.OrdinalIgnoreCase));
        release.Release();
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Not_Report_A_Drain_Failure_When_Nothing_Is_In_Flight()
    {
        await _sut.SubscribeAsync(
            PubSubName,
            Topic,
            (payload, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success));

        await _sut.UnsubscribeAsync(PubSubName, Topic);

        _logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Error);
    }
}
