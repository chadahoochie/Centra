namespace RabbitMQ.Benchmark.Contracts;

public readonly record struct LatencyStatistics(
    double MeanMs,
    double P50Ms,
    double P90Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);
