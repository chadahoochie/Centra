using RabbitMQ.Benchmark.Contracts;

namespace PackageQueueing.Benchmarks.RabbitMQ;

public sealed class PackageQueueingBenchmarkArguments
{
    public string Scenario { get; set; } = "producer";

    public Uri RabbitUri { get; set; } = new("amqp://guest:guest@localhost:5672/");

    public int Messages { get; set; } = 20_000;

    public int Concurrency { get; set; } = 4;

    public int PayloadSize { get; set; } = 1024;

    public bool PublisherConfirms { get; set; }

    public BenchmarkQueueType QueueType { get; set; } = BenchmarkQueueType.Classic;

    public string? OutputJsonPath { get; set; }

    public bool RunMicroBenchmarks { get; set; }
}
