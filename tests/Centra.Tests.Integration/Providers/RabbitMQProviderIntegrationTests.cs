using System.Text;
using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shouldly;
using Testcontainers.RabbitMq;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class RabbitMQProviderIntegrationTests : IAsyncLifetime
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

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMQProviderOptions
        {
            ExchangeName = "centra.pubsub.it",
            DefaultPubSubName = "pubsub",
            QueuePrefix = "centra-it"
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
    public async Task Should_Publish_And_Receive_CloudEvents_Via_RabbitMQ_Topic_Exchange()
    {
        _driver.ShouldNotBeNull();
        const string pubSub = "pubsub";
        const string topic = "orders.integration.created";
        var payload = Encoding.UTF8.GetBytes("{\"orderId\":\"rmq-order-999\",\"amount\":150.00}");
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = "rmq-evt-001",
            ["ce-type"] = "orders.created",
            ["ce-source"] = "centra://orders",
            ["ce-specversion"] = "1.0",
            ["ce-correlationid"] = "corr-rmq-001",
            ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
        };

        var receivedTcs = new TaskCompletionSource<(byte[] Payload, IReadOnlyDictionary<string, string> Headers)>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 1. Subscribe to topic
        await _driver.SubscribeAsync(pubSub, topic, (body, hdrs, ct) =>
        {
            receivedTcs.TrySetResult((body.ToArray(), hdrs));
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        // 2. Publish CloudEvent
        await _driver.PublishAsync(pubSub, topic, payload, headers);

        // 3. Await receipt
        var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(10000));
        completed.ShouldBe(receivedTcs.Task);

        var received = await receivedTcs.Task;
        received.Payload.ShouldBe(payload);
        received.Headers["ce-id"].ShouldBe("rmq-evt-001");
        received.Headers["ce-type"].ShouldBe("orders.created");
        received.Headers["ce-correlationid"].ShouldBe("corr-rmq-001");
        received.Headers["traceparent"].ShouldBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    }
}
