using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Benchmark.Contracts;
using VaxCare.Queueing;
using VaxCare.Queueing.Core;

namespace PackageQueueing.Benchmarks.RabbitMQ;

[MemoryDiagnoser]
public class PackageQueueingMicroBenchmarks
{
    private ServiceProvider? _serviceProvider;
    private IEnqueuer<BenchmarkOrderMessage>? _enqueuer;
    private BenchmarkOrderMessage? _message;

    [Params(256, 1024, 4096)]
    public int PayloadSizeBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rabbitUri = new Uri(Environment.GetEnvironmentVariable("RABBITMQ_URI") ?? "amqp://guest:guest@localhost:5672/");
        var exchangeName = "bench-pkg-micro";
        var routingKey = "orders.pkg.micro";

        var services = new ServiceCollection();
        services.AddVaxCareQueueing(cfg =>
        {
            cfg.Uri = rabbitUri;
            cfg.RabbitMqConnectionLabel = "pkg-bench-micro";
        }, q =>
        {
            q.AddEnqueuer<BenchmarkOrderMessage>(exchangeName, routingKey);
        });

        _serviceProvider = services.BuildServiceProvider();
        _enqueuer = _serviceProvider.GetRequiredService<IEnqueuer<BenchmarkOrderMessage>>();

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

    [Benchmark(Description = "Package.Queueing.Enqueue (Pipelining Steppers)")]
    public void Enqueue()
    {
        _enqueuer!.Enqueue(_message!);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }
}
