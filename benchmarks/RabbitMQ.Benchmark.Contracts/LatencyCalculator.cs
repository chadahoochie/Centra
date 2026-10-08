namespace RabbitMQ.Benchmark.Contracts;

public static class LatencyCalculator
{
    public static LatencyStatistics Calculate(double[] timingsMs)
    {
        if (timingsMs.Length == 0)
        {
            return new LatencyStatistics(0, 0, 0, 0, 0, 0);
        }

        Array.Sort(timingsMs);

        var count = timingsMs.Length;
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            sum += timingsMs[i];
        }

        var mean = sum / count;
        var p50 = timingsMs[(int)(count * 0.50)];
        var p90 = timingsMs[Math.Min(count - 1, (int)(count * 0.90))];
        var p95 = timingsMs[Math.Min(count - 1, (int)(count * 0.95))];
        var p99 = timingsMs[Math.Min(count - 1, (int)(count * 0.99))];
        var max = timingsMs[count - 1];

        return new LatencyStatistics(mean, p50, p90, p95, p99, max);
    }
}
