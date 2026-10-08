using RabbitMQ.Benchmark.Contracts;

namespace RabbitMQ.Benchmark.Orchestrator;

public sealed class OrchestratorArguments
{
    public Uri? RabbitUri { get; set; }

    public int Messages { get; set; } = 20_000;

    public int Concurrency { get; set; } = 4;

    public int PayloadSize { get; set; } = 1024;

    public bool PublisherConfirms { get; set; }

    public List<BenchmarkQueueType> QueueTypes { get; set; } = [BenchmarkQueueType.Classic];

    public string Suite { get; set; } = "all";

    public string OutputDir { get; set; } = "benchmarks/output";
}
