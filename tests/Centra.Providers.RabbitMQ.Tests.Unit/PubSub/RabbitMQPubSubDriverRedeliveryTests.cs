using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class RabbitMQPubSubDriverRedeliveryTests
{
    private const string QueueName = "centra.pubsub.orders.created";

    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly RabbitMQProviderOptions _options = new();
    private readonly RabbitMQPubSubDriver _sut;

    public RabbitMQPubSubDriverRedeliveryTests()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        _sut = new RabbitMQPubSubDriver(connectionFactory, Microsoft.Extensions.Options.Options.Create(_options));
    }

    internal static BasicDeliverEventArgs Delivery(string? cloudEventId, ulong deliveryTag)
    {
        var properties = new BasicProperties();
        if (cloudEventId is not null)
        {
            properties.Headers = new Dictionary<string, object?> { ["ce-id"] = cloudEventId };
        }

        return new BasicDeliverEventArgs(
            consumerTag: "consumer-tag",
            deliveryTag: deliveryTag,
            redelivered: false,
            exchange: "centra.pubsub",
            routingKey: "orders.created",
            properties: properties,
            body: ReadOnlyMemory<byte>.Empty);
    }

    [Fact]
    public async Task A_Budgeted_Subscription_DeadLetters_After_Exhausting_Its_Budget()
    {
        var policy = new RedeliveryBudgetPolicy(MaxRetryAttempts: 3, InitialBackoff: TimeSpan.Zero, MaxBackoff: TimeSpan.Zero);
        var invocations = 0;

        for (ulong tag = 1; tag <= 4; tag++)
        {
            await _sut.ProcessAndAckAsync(
                _channel,
                QueueName,
                Delivery("evt-1", tag),
                (_, _, _) =>
                {
                    invocations++;
                    return ValueTask.FromResult(EventHandlingResult.Retry);
                },
                policy,
                CancellationToken.None);
        }

        invocations.ShouldBe(4);
        await _channel.Received(3).BasicNackAsync(Arg.Any<ulong>(), multiple: false, requeue: true);
        await _channel.Received(1).BasicRejectAsync(4UL, requeue: false);
    }

    [Fact]
    public async Task A_Subscription_With_The_Budget_Disabled_Requeues_Forever_And_Never_DeadLetters()
    {
        var disabled = new RedeliveryBudgetPolicy(MaxRetryAttempts: 0, InitialBackoff: TimeSpan.Zero, MaxBackoff: TimeSpan.Zero);

        for (ulong tag = 1; tag <= 10; tag++)
        {
            await _sut.ProcessAndAckAsync(
                _channel,
                QueueName,
                Delivery("evt-1", tag),
                (_, _, _) => ValueTask.FromResult(EventHandlingResult.Retry),
                disabled,
                CancellationToken.None);
        }

        await _channel.Received(10).BasicNackAsync(Arg.Any<ulong>(), multiple: false, requeue: true);
        await _channel.DidNotReceive().BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task A_Subscription_With_The_Budget_Disabled_Requeues_An_Identityless_Delivery_Instead_Of_DeadLettering()
    {
        var disabled = new RedeliveryBudgetPolicy(MaxRetryAttempts: 0, InitialBackoff: TimeSpan.Zero, MaxBackoff: TimeSpan.Zero);

        await _sut.ProcessAndAckAsync(
            _channel,
            QueueName,
            Delivery(cloudEventId: null, deliveryTag: 7),
            (_, _, _) => ValueTask.FromResult(EventHandlingResult.Retry),
            disabled,
            CancellationToken.None);

        await _channel.Received(1).BasicNackAsync(7UL, multiple: false, requeue: true);
        await _channel.DidNotReceive().BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task A_Drain_During_Backoff_Leaves_The_Delivery_Unsettled_And_Releases_Its_Budget_Entry()
    {
        var policy = new RedeliveryBudgetPolicy(
            MaxRetryAttempts: 3,
            InitialBackoff: TimeSpan.FromMinutes(5),
            MaxBackoff: TimeSpan.FromMinutes(5));
        var budget = new BoundedRedeliveryBudget();
        var driver = new RabbitMQPubSubDriver(
            Substitute.For<IConnectionFactory>(),
            Microsoft.Extensions.Options.Options.Create(_options),
            redeliveryBudget: budget);

        using var shutdown = new CancellationTokenSource();
        var processing = driver.ProcessAndAckAsync(
            _channel,
            QueueName,
            Delivery("evt-1", 1),
            (_, _, _) => ValueTask.FromResult(EventHandlingResult.Retry),
            policy,
            shutdown.Token);

        await shutdown.CancelAsync();
        await processing;

        await _channel.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>());
        await _channel.DidNotReceive().BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>());

        budget.ChargeFailure(new RedeliveryBudgetKey(QueueName, "evt-1"), policy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromMinutes(5)));
    }
}
