using System.Text;
using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using RabbitMQ.Client;
using Shouldly;
using Testcontainers.RabbitMq;
using Xunit;

namespace Centra.Tests.Integration.Providers;

/// <summary>
/// Proves the shutdown contract against a real broker: handlers already running when the
/// subscription is torn down finish and are acknowledged, so the broker has nothing to redeliver.
/// </summary>
public sealed class RabbitMQGracefulDrainIntegrationTests : IAsyncLifetime
{
    private const string PubSub = "pubsub";
    private const int SlowMessageCount = 5;

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.3-management").Build();

    private RabbitMQPubSubDriver? _driver;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var factory = new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) };
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMQProviderOptions
        {
            ExchangeName = "centra.pubsub.drain.it",
            DefaultPubSubName = PubSub,
            QueuePrefix = "centra-drain-it",
            TotalShutdownDrainTimeout = TimeSpan.FromSeconds(30)
        });

        _driver = new RabbitMQPubSubDriver(factory, options);
    }

    public async Task DisposeAsync()
    {
        if (_driver is not null)
        {
            await _driver.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Drain_Slow_Handlers_And_Leave_Nothing_To_Redeliver()
    {
        _driver.ShouldNotBeNull();
        const string topic = "orders.drain.created";

        var started = 0;
        var completed = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await _driver.SubscribeAsync(
            PubSub,
            topic,
            async (payload, headers, ct) =>
            {
                if (Interlocked.Increment(ref started) == SlowMessageCount)
                {
                    allStarted.TrySetResult();
                }

                await Task.Delay(1500, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref completed);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { MaxConcurrentCalls = SlowMessageCount, PrefetchCount = SlowMessageCount });

        for (var i = 0; i < SlowMessageCount; i++)
        {
            await _driver.PublishAsync(
                PubSub,
                topic,
                Encoding.UTF8.GetBytes($"{{\"n\":{i}}}"),
                new Dictionary<string, string> { ["ce-id"] = $"drain-{i}" });
        }

        var reachedAllStarted = await Task.WhenAny(allStarted.Task, Task.Delay(30_000));
        reachedAllStarted.ShouldBe(allStarted.Task);
        Volatile.Read(ref completed).ShouldBe(0, "the drain must be exercised while every handler is still mid-flight");

        await _driver.UnsubscribeAsync(PubSub, topic);

        Volatile.Read(ref completed).ShouldBe(SlowMessageCount);

        // Acked before the channel closed, so the broker has neither ready nor unacked work left.
        var stats = await _driver.GetQueueStatsAsync(PubSub, topic);
        stats.ShouldNotBeNull();
        stats.Value.MessageCount.ShouldBe(0);

        var redelivered = 0;
        await _driver.SubscribeAsync(
            PubSub,
            topic,
            (payload, headers, ct) =>
            {
                Interlocked.Increment(ref redelivered);
                return ValueTask.FromResult(EventHandlingResult.Success);
            });

        await Task.Delay(3000);
        Volatile.Read(ref redelivered).ShouldBe(0);
    }

    [Fact]
    public async Task UnsubscribeAsync_Should_Drain_Prefetched_Messages_Not_Yet_Handed_To_A_Handler()
    {
        _driver.ShouldNotBeNull();
        const string topic = "orders.drain.prefetched";
        const int messageCount = 12;

        var completed = 0;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Prefetch far above the concurrency limit - the shipped default shape - so most messages sit
        // in the client's consumer dispatcher, buffered but never handed to a handler, at shutdown.
        await _driver.SubscribeAsync(
            PubSub,
            topic,
            async (payload, headers, ct) =>
            {
                firstStarted.TrySetResult();
                await Task.Delay(150, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref completed);
                return EventHandlingResult.Success;
            },
            options: new PubSubSubscribeOptions { MaxConcurrentCalls = 1, PrefetchCount = messageCount * 4 });

        for (var i = 0; i < messageCount; i++)
        {
            await _driver.PublishAsync(
                PubSub,
                topic,
                Encoding.UTF8.GetBytes($"{{\"n\":{i}}}"),
                new Dictionary<string, string> { ["ce-id"] = $"prefetched-{i}" });
        }

        var reachedFirstStarted = await Task.WhenAny(firstStarted.Task, Task.Delay(30_000));
        reachedFirstStarted.ShouldBe(firstStarted.Task);

        await _driver.UnsubscribeAsync(PubSub, topic);

        Volatile.Read(ref completed).ShouldBe(messageCount);

        var stats = await _driver.GetQueueStatsAsync(PubSub, topic);
        stats.ShouldNotBeNull();
        stats.Value.MessageCount.ShouldBe(0);
    }
}
