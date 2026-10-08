using BenchmarkDotNet.Attributes;
using Centra.Drivers;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using RabbitMQ.Benchmark.Contracts;

namespace Centra.Benchmarks.RabbitMQ;

[MemoryDiagnoser]
public class CentraMicroBenchmarks
{
    private RabbitMQPubSubDriver? _driver;
    private IPubSubClient? _client;
    private BenchmarkOrderMessage? _message;

    [Params(256, 1024, 4096)]
    public int PayloadSizeBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rabbitUri = new Uri(Environment.GetEnvironmentVariable("RABBITMQ_URI") ?? "amqp://guest:guest@localhost:5672/");
        var exchangeName = "bench-centra-micro";
        var (driver, client) = CentraBenchmarkDriverFactory.Create(
            rabbitUri,
            exchangeName,
            queuePrefix: "centra-micro",
            BenchmarkQueueType.Classic);

        _driver = driver;
        _client = client;

        var payload = new byte[PayloadSizeBytes];
        Random.Shared.NextBytes(payload);

        _message = new BenchmarkOrderMessage
        {
            Id = Guid.NewGuid(),
            CustomerId = "cust-micro-001",
            Amount = 49.99m,
            SentTimestampTicks = 0,
            Payload = payload
        };
    }

    [Benchmark(Description = "Centra.PublishAsync (CloudEvents Binary)")]
    public async Task PublishAsync()
    {
        await _client!.PublishAsync("orders.micro", _message!, options: new PubSubPublishOptions
        {
            Mode = Centra.Events.CloudEventMode.Binary
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _driver?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
