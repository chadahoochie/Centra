using System.Diagnostics;
using Centra.PubSub;
using RabbitMQ.Benchmark.Contracts;

namespace Centra.Benchmarks.RabbitMQ;

public static class CentraProducerScenario
{
    public static async Task<BenchmarkScenarioResult> RunAsync(
        Uri rabbitUri,
        int messageCount,
        int concurrency,
        int payloadSizeBytes,
        bool publisherConfirms,
        BenchmarkQueueType queueType)
    {
        var exchangeName = $"bench-centra-prod-{Guid.NewGuid():N}";
        var topic = "orders.bench";

        var (driver, client) = CentraBenchmarkDriverFactory.Create(
            rabbitUri,
            exchangeName,
            queuePrefix: $"centra-bench-{Guid.NewGuid():N}",
            queueType);

        try
        {
            // Subscribe a draining dummy subscriber so published messages do not accumulate indefinitely
            await driver.SubscribeAsync(
                "pubsub",
                topic,
                (body, headers, ct) => ValueTask.FromResult(EventHandlingResult.Success),
                options: new PubSubSubscribeOptions
                {
                    MaxConcurrentCalls = concurrency,
                    AutoDelete = queueType != BenchmarkQueueType.Quorum,
                    CustomArguments = queueType == BenchmarkQueueType.Quorum
                        ? new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }
                        : null
                });

            var payloadBytes = new byte[payloadSizeBytes];
            Random.Shared.NextBytes(payloadBytes);

            var timingsMs = new double[messageCount];
            var errorCount = 0;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memBefore = MemorySnapshot.Capture();
            var swTotal = Stopwatch.StartNew();

            await Parallel.ForAsync(0, messageCount, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (i, ct) =>
            {
                var msg = new BenchmarkOrderMessage
                {
                    Id = Guid.NewGuid(),
                    CustomerId = $"cust-{i:D6}",
                    Amount = 19.99m + (i % 100),
                    SentTimestampTicks = Stopwatch.GetTimestamp(),
                    Payload = payloadBytes
                };

                var swMsg = Stopwatch.GetTimestamp();
                try
                {
                    await client.PublishAsync(
                        topic,
                        msg,
                        options: new PubSubPublishOptions { Mode = Centra.Events.CloudEventMode.Binary },
                        cancellationToken: ct);

                    timingsMs[i] = Stopwatch.GetElapsedTime(swMsg).TotalMilliseconds;
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                    timingsMs[i] = Stopwatch.GetElapsedTime(swMsg).TotalMilliseconds;
                }
            });

            swTotal.Stop();
            var memAfter = MemorySnapshot.Capture();
            var memDelta = MemorySnapshot.ComputeDelta(memBefore, memAfter);

            var elapsedMs = swTotal.Elapsed.TotalMilliseconds;
            var throughput = elapsedMs > 0 ? (messageCount / (elapsedMs / 1000.0)) : 0;
            var latencyStats = LatencyCalculator.Calculate(timingsMs);
            var workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

            return new BenchmarkScenarioResult
            {
                Framework = "Centra",
                Scenario = "Producer",
                QueueType = queueType.ToString(),
                Concurrency = concurrency,
                PayloadSizeBytes = payloadSizeBytes,
                PublisherConfirms = publisherConfirms,
                TotalMessages = messageCount,
                ElapsedMilliseconds = elapsedMs,
                ThroughputMsgPerSec = throughput,
                LatencyMeanMs = latencyStats.MeanMs,
                LatencyP50Ms = latencyStats.P50Ms,
                LatencyP90Ms = latencyStats.P90Ms,
                LatencyP95Ms = latencyStats.P95Ms,
                LatencyP99Ms = latencyStats.P99Ms,
                LatencyMaxMs = latencyStats.MaxMs,
                TotalAllocatedBytes = memDelta.AllocatedBytes,
                AllocatedBytesPerMessage = messageCount > 0 ? (double)memDelta.AllocatedBytes / messageCount : 0,
                Gen0Collections = memDelta.Gen0,
                Gen1Collections = memDelta.Gen1,
                Gen2Collections = memDelta.Gen2,
                PeakWorkingSetMb = workingSetMb,
                ErrorCount = errorCount
            };
        }
        finally
        {
            await driver.DisposeAsync();
        }
    }
}
