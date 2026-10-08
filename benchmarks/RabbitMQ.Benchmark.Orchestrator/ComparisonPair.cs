using RabbitMQ.Benchmark.Contracts;

namespace RabbitMQ.Benchmark.Orchestrator;

public sealed class ComparisonPair
{
    public required BenchmarkScenarioResult Centra { get; init; }

    public required BenchmarkScenarioResult PackageQueueing { get; init; }

    public double ThroughputMultiplier => PackageQueueing.ThroughputMsgPerSec > 0
        ? Centra.ThroughputMsgPerSec / PackageQueueing.ThroughputMsgPerSec
        : 1.0;

    public double ThroughputDeltaPct => PackageQueueing.ThroughputMsgPerSec > 0
        ? ((Centra.ThroughputMsgPerSec - PackageQueueing.ThroughputMsgPerSec) / PackageQueueing.ThroughputMsgPerSec) * 100.0
        : 0.0;

    public double LatencyP50ReductionPct => PackageQueueing.LatencyP50Ms > 0
        ? ((PackageQueueing.LatencyP50Ms - Centra.LatencyP50Ms) / PackageQueueing.LatencyP50Ms) * 100.0
        : 0.0;

    public double LatencyP95ReductionPct => PackageQueueing.LatencyP95Ms > 0
        ? ((PackageQueueing.LatencyP95Ms - Centra.LatencyP95Ms) / PackageQueueing.LatencyP95Ms) * 100.0
        : 0.0;

    public double AllocationReductionPct => PackageQueueing.AllocatedBytesPerMessage > 0
        ? ((PackageQueueing.AllocatedBytesPerMessage - Centra.AllocatedBytesPerMessage) / PackageQueueing.AllocatedBytesPerMessage) * 100.0
        : 0.0;
}
