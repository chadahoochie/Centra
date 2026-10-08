using Centra.Drivers;
using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Benchmark.Contracts;
using RabbitMQ.Client;

namespace Centra.Benchmarks.RabbitMQ;

public static class CentraBenchmarkDriverFactory
{
    public static (RabbitMQPubSubDriver Driver, IPubSubClient Client) Create(
        Uri rabbitUri,
        string exchangeName,
        string queuePrefix,
        BenchmarkQueueType queueType)
    {
        var factory = new ConnectionFactory
        {
            Uri = rabbitUri
        };

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMQProviderOptions
        {
            ExchangeName = exchangeName,
            DefaultPubSubName = "pubsub",
            QueuePrefix = queuePrefix
        });

        var driver = new RabbitMQPubSubDriver(
            factory,
            options,
            NullLogger<RabbitMQPubSubDriver>.Instance);

        var registry = new ComponentRegistry();
        registry.RegisterPubSubDriver("pubsub", driver);

        var client = new CentraPubSubClient(
            registry,
            appId: "centra-benchmark",
            defaultPubSubName: "pubsub");

        return (driver, client);
    }
}
