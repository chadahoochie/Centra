namespace RabbitMQ.Benchmark.Contracts;

public sealed class BenchmarkScenarioResult
{
    public string Framework { get; set; } = string.Empty;

    public string Scenario { get; set; } = string.Empty;

    public string QueueType { get; set; } = string.Empty;

    public int Concurrency { get; set; }

    public int PayloadSizeBytes { get; set; }

    public bool PublisherConfirms { get; set; }

    public int TotalMessages { get; set; }

    public double ElapsedMilliseconds { get; set; }

    public double ThroughputMsgPerSec { get; set; }

    public double LatencyMeanMs { get; set; }

    public double LatencyP50Ms { get; set; }

    public double LatencyP90Ms { get; set; }

    public double LatencyP95Ms { get; set; }

    public double LatencyP99Ms { get; set; }

    public double LatencyMaxMs { get; set; }

    public long TotalAllocatedBytes { get; set; }

    public double AllocatedBytesPerMessage { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    public double PeakWorkingSetMb { get; set; }

    public int ErrorCount { get; set; }
}
