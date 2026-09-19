using System.Diagnostics;
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
/// Covers the subscription shutdown contract: cancel the consumer so no new deliveries arrive, let the
/// client finish handing over the deliveries it had already buffered, then await those handlers up to a
/// configurable budget before the channel is closed.
/// </summary>
public sealed class RabbitMQPubSubDriverDrainTests
{
    private const string PubSubName = "pubsub";
    private const string Topic = "orders.created";

    private readonly IChannel _channel;
    private readonly RabbitMQProviderOptions _options;
    private readonly RecordingLogger<RabbitMQPubSubDriver> _logger;
    private readonly RabbitMQPubSubDriver _sut;
    private readonly Dictionary<string, IAsyncBasicConsumer> _consumers = new(StringComparer.Ordinal);

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
            Arg.Any<IAsyncBasicConsumer>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var tag = $"consumer-tag-{_consumers.Count + 1}";
                _consumers[tag] = call.Arg<IAsyncBasicConsumer>();
                return tag;
            });

        // The real client routes cancel-ok back through the consumer, which is what tells the driver
        // that nothing more will be dispatched. Tests that need cancel-ok ordered behind buffered
        // deliveries re-stub this to enqueue it on a ConsumerDispatchQueue instead.
        _channel.BasicCancelAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var tag = call.Arg<string>();
                return _consumers.TryGetValue(tag, out var consumer)
                    ? consumer.HandleBasicCancelOkAsync(tag)
                    : Task.CompletedTask;
            });

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

        var (tag, consumer) = _consumers.Single();
        await new ConsumerDeliverySource(consumer, tag).DeliverAsync(k, _options.ExchangeName, Topic);

        await _sut.UnsubscribeAsync(PubSubName, Topic);

        Volatile.Read(ref completed).ShouldBe(k);
        completedWhenChannelClosed.ShouldBe(k);
        await _channel.Received(k).BasicAckAsync(Arg.Any<ulong>(), multiple: false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), requeue: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Drain_Deliveries_Still_Queued_In_The_Consumer_Dispatcher()
    {
        // The shipped default shape: prefetch far above the concurrency limit, so at shutdown most
        // deliveries have been read off the socket but not yet handed to a handler.
        const int prefetched = 20;
        var completed = 0;
        var completedWhenChannelClosed = -1;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _channel
            .When(c => c.CloseAsync(Arg.Any<ushort>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()))
            .Do(_ => completedWhenChannelClosed = Volatile.Read(ref completed));

        await _sut.SubscribeAsync(
            PubSubName,
            Topic,
            async (payload, headers, ct) =>
            {
                firstStarted.TrySetResult();
                await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref completed);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { PrefetchCount = prefetched, MaxConcurrentCalls = 1 });

        var (tag, consumer) = _consumers.Single();
        await using var dispatcher = new ConsumerDispatchQueue(consumer, tag);
        _channel.BasicCancelAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dispatcher.EnqueueCancelOk();
                return Task.CompletedTask;
            });

        dispatcher.EnqueueDeliveries(prefetched, _options.ExchangeName, Topic);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await _sut.UnsubscribeAsync(PubSubName, Topic);

        Volatile.Read(ref completed).ShouldBe(prefetched);
        completedWhenChannelClosed.ShouldBe(prefetched);
        await _channel.Received(prefetched).BasicAckAsync(Arg.Any<ulong>(), multiple: false, Arg.Any<CancellationToken>());
        _logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Error);
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

        var (tag, consumer) = _consumers.Single();
        await new ConsumerDeliverySource(consumer, tag).DeliverAsync(k, _options.ExchangeName, Topic);

        await _sut.DisposeAsync();

        Volatile.Read(ref completed).ShouldBe(k);
        await _channel.Received(1).BasicCancelAsync(tag, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _channel.Received(k).BasicAckAsync(Arg.Any<ulong>(), multiple: false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_Should_Share_One_Drain_Budget_Across_Every_Subscription()
    {
        const int subscriptionCount = 4;
        var budget = TimeSpan.FromMilliseconds(500);
        _options.TotalShutdownDrainTimeout = budget;
        using var release = new SemaphoreSlim(0, subscriptionCount);

        for (var i = 0; i < subscriptionCount; i++)
        {
            await _sut.SubscribeAsync(
                PubSubName,
                $"{Topic}.{i}",
                async (payload, headers, ct) =>
                {
                    await release.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
                    return EventHandlingResult.Success;
                },
                options: new PubSubSubscribeOptions { MaxConcurrentCalls = 2 });
        }

        _consumers.Count.ShouldBe(subscriptionCount);
        foreach (var (tag, consumer) in _consumers)
        {
            await new ConsumerDeliverySource(consumer, tag).DeliverAsync(1, _options.ExchangeName, Topic);
        }

        var elapsed = Stopwatch.StartNew();
        await _sut.DisposeAsync();
        elapsed.Stop();

        // One shared budget, not one per subscription: two budgets is already a generous ceiling.
        elapsed.Elapsed.ShouldBeLessThan(budget * 2);
        release.Release(subscriptionCount);
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Share_One_Drain_Budget_Across_An_Open_Shutdown_Window()
    {
        // The host teardown shape: one UnsubscribeAsync per topic, inside a single shutdown window.
        const int subscriptionCount = 4;
        var budget = TimeSpan.FromMilliseconds(500);
        _options.TotalShutdownDrainTimeout = budget;
        using var release = new SemaphoreSlim(0, subscriptionCount);
        var topics = new List<string>();

        for (var i = 0; i < subscriptionCount; i++)
        {
            var topic = $"{Topic}.{i}";
            topics.Add(topic);
            await _sut.SubscribeAsync(
                PubSubName,
                topic,
                async (payload, headers, ct) =>
                {
                    await release.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
                    return EventHandlingResult.Success;
                },
                options: new PubSubSubscribeOptions { MaxConcurrentCalls = 2 });
        }

        _consumers.Count.ShouldBe(subscriptionCount);
        foreach (var (tag, consumer) in _consumers)
        {
            await new ConsumerDeliverySource(consumer, tag).DeliverAsync(1, _options.ExchangeName, Topic);
        }

        var elapsed = Stopwatch.StartNew();
        using (_sut.BeginShutdownDrain())
        {
            foreach (var topic in topics)
            {
                await _sut.UnsubscribeAsync(PubSubName, topic);
            }
        }
        elapsed.Stop();

        elapsed.Elapsed.ShouldBeLessThan(budget * 2);
        release.Release(subscriptionCount);
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Get_The_Whole_Budget_Once_The_Shutdown_Window_Is_Closed()
    {
        var budget = TimeSpan.FromMilliseconds(400);
        _options.TotalShutdownDrainTimeout = budget;
        using var release = new SemaphoreSlim(0, 1);

        _sut.BeginShutdownDrain().Dispose();

        await _sut.SubscribeAsync(
            PubSubName,
            Topic,
            async (payload, headers, ct) =>
            {
                await release.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { MaxConcurrentCalls = 2 });

        var (tag, consumer) = _consumers.Single();
        await new ConsumerDeliverySource(consumer, tag).DeliverAsync(1, _options.ExchangeName, Topic);

        var elapsed = Stopwatch.StartNew();
        await _sut.UnsubscribeAsync(PubSubName, Topic);
        elapsed.Stop();

        elapsed.Elapsed.ShouldBeGreaterThanOrEqualTo(budget - TimeSpan.FromMilliseconds(100));
        release.Release();
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Report_When_The_Drain_Timeout_Is_Exceeded()
    {
        _options.TotalShutdownDrainTimeout = TimeSpan.FromMilliseconds(100);
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

        var (tag, consumer) = _consumers.Single();
        await new ConsumerDeliverySource(consumer, tag).DeliverAsync(1, _options.ExchangeName, Topic);

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
