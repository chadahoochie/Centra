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
/// Reproduction of the unbounded nack-requeue loop: RabbitMQ only advances <c>x-delivery-count</c>
/// on consumer/channel failure, never on an application <c>basic.nack(requeue=true)</c>, so
/// <c>x-delivery-limit</c> cannot bound <see cref="EventHandlingResult.Retry"/>. The budget must be
/// enforced by the consumer.
/// </summary>
public sealed class RabbitMQRedeliveryBudgetIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.3-management")
        .Build();

    private RabbitMQPubSubDriver? _driver;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_container.GetConnectionString())
        };

        _driver = new RabbitMQPubSubDriver(
            factory,
            Microsoft.Extensions.Options.Options.Create(new RabbitMQProviderOptions
            {
                ExchangeName = "centra.pubsub.retrybudget.it",
                DefaultPubSubName = "pubsub",
                QueuePrefix = "centra-retrybudget-it"
            }));
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
    public async Task Retry_Should_Be_Bounded_To_Three_Attempts_Then_DeadLetter()
    {
        _driver.ShouldNotBeNull();
        const string pubSub = "pubsub";
        const string topic = "orders.retrybudget.created";
        const string deadLetterTopic = "orders.retrybudget.created.dead";

        var deadLettered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadLetterCount = 0;
        var handlerInvocations = 0;

        // The dead-letter consumer must exist before the budget is exhausted, otherwise the
        // rejected message has nowhere to land.
        await _driver.SubscribeAsync(pubSub, deadLetterTopic, (_, _, _) =>
        {
            Interlocked.Increment(ref deadLetterCount);
            deadLettered.TrySetResult(true);
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        // x-delivery-limit stays on the queue as a backstop against channel-failure loops only -
        // nothing below depends on it for application-level retry.
        await _driver.SubscribeAsync(
            pubSub,
            topic,
            (_, _, _) =>
            {
                Interlocked.Increment(ref handlerInvocations);
                return ValueTask.FromResult(EventHandlingResult.Retry);
            },
            deadLetterTopic: deadLetterTopic,
            options: new PubSubSubscribeOptions
            {
                MaxRetryAttempts = 3,
                RetryInitialBackoff = TimeSpan.FromMilliseconds(50),
                RetryMaxBackoff = TimeSpan.FromMilliseconds(200),
                CustomArguments = new Dictionary<string, object?>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-delivery-limit"] = 3
                }
            });

        await _driver.PublishAsync(
            pubSub,
            topic,
            Encoding.UTF8.GetBytes("{\"orderId\":\"retry-budget-1\"}"),
            new Dictionary<string, string>
            {
                ["ce-id"] = "retry-budget-evt-001",
                ["ce-type"] = "orders.created",
                ["ce-source"] = "centra://orders",
                ["ce-specversion"] = "1.0"
            });

        // Without a consumer-enforced budget this never completes: the nack-requeue loop was measured at
        // 2,710 handler invocations inside this same window, with nothing ever reaching the dead-letter queue.
        await deadLettered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Volatile.Read(ref handlerInvocations).ShouldBe(4);
        Volatile.Read(ref deadLetterCount).ShouldBe(1);

        // The loop really ended rather than merely reaching the dead-letter queue once: the rejected message
        // left the source queue instead of being requeued again.
        var sourceQueue = await _driver.GetQueueStatsAsync(pubSub, topic);
        sourceQueue.ShouldNotBeNull();
        sourceQueue.Value.MessageCount.ShouldBe(0);
    }
}
