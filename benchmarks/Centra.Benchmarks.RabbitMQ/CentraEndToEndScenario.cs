using System.Diagnostics;
using Centra.PubSub;
using RabbitMQ.Benchmark.Contracts;

namespace Centra.Benchmarks.RabbitMQ;

public static class CentraEndToEndScenario
{
    public static async Task<BenchmarkScenarioResult> RunAsync(
        Uri rabbitUri,
        int messageCount,
        int concurrency,
        int payloadSizeBytes,
        bool publisherConfirms,
        BenchmarkQueueType queueType)
    {
        var exchangeName = $"bench-centra-e2e-{Guid.NewGuid():N}";
        var topic = "orders.e2e";

        var (driver, client) = CentraBenchmarkDriverFactory.Create(
            rabbitUri,
            exchangeName,
            queuePrefix: $"centra-e2e-{Guid.NewGuid():N}",
            queueType);

        try
        {
            var payloadBytes = new byte[payloadSizeBytes];
            Random.Shared.NextBytes(payloadBytes);

            var transitTimingsMs = new double[messageCount];
            var receivedCount = 0;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await driver.SubscribeAsync(
                "pubsub",
                topic,
                (body, headers, ct) =>
                {
                    var receiveTs = Stopwatch.GetTimestamp();
                    var index = Interlocked.Increment(ref receivedCount) - 1;

                    if (headers.TryGetValue("ce-sentticks", out var sentTicksStr) && long.TryParse(sentTicksStr, out var sentTicks))
                    {
                        var latency = Stopwatch.GetElapsedTime(sentTicks, receiveTs).TotalMilliseconds;
                        if (index < transitTimingsMs.Length)
                        {
                            transitTimingsMs[index] = latency;
                        }
                    }

                    if (receivedCount >= messageCount)
                    {
                        tcs.TrySetResult();
                    }

                    return ValueTask.FromResult(EventHandlingResult.Success);
                },
                options: new PubSubSubscribeOptions
                {
                    MaxConcurrentCalls = concurrency,
                    PrefetchCount = concurrency * 30,
                    AutoDelete = queueType != BenchmarkQueueType.Quorum,
                    CustomArguments = queueType == BenchmarkQueueType.Quorum
                        ? new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }
                        : null
                });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memBefore = MemorySnapshot.Capture();
            var swTotal = Stopwatch.StartNew();

            // Publish concurrently
            var publishTask = Parallel.ForAsync(0, messageCount, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (i, ct) =>
            {
                var msg = new BenchmarkOrderMessage
                {
                    Id = Guid.NewGuid(),
                    CustomerId = $"cust-{i:D6}",
                    Amount = 25.50m,
                    SentTimestampTicks = Stopwatch.GetTimestamp(),
                    Payload = payloadBytes
                };

                var options = new PubSubPublishOptions
                {
                    Mode = Centra.Events.CloudEventMode.Binary,
                    Metadata = new Dictionary<string, string>
                    {
                        ["ce-sentticks"] = msg.SentTimestampTicks.ToString()
                    }
                };

                await client.PublishAsync(topic, msg, options, ct);
            });

            await publishTask;
            var timeoutSeconds = Math.Max(30, messageCount / 10);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            if (completed != tcs.Task)
            {
                Console.WriteLine($"[Centra] Warning: EndToEnd scenario timed out waiting for all messages. Received {receivedCount}/{messageCount}");
            }
            swTotal.Stop();

            var memAfter = MemorySnapshot.Capture();
            var memDelta = MemorySnapshot.ComputeDelta(memBefore, memAfter);

            var elapsedMs = swTotal.Elapsed.TotalMilliseconds;
            var throughput = elapsedMs > 0 ? (receivedCount / (elapsedMs / 1000.0)) : 0;
            var latencyStats = LatencyCalculator.Calculate(transitTimingsMs[..receivedCount]);
            var workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

            return new BenchmarkScenarioResult
            {
                Framework = "Centra",
                Scenario = "EndToEnd",
                QueueType = queueType.ToString(),
                Concurrency = concurrency,
                PayloadSizeBytes = payloadSizeBytes,
                PublisherConfirms = publisherConfirms,
                TotalMessages = receivedCount,
                ElapsedMilliseconds = elapsedMs,
                ThroughputMsgPerSec = throughput,
                LatencyMeanMs = latencyStats.MeanMs,
                LatencyP50Ms = latencyStats.P50Ms,
                LatencyP90Ms = latencyStats.P90Ms,
                LatencyP95Ms = latencyStats.P95Ms,
                LatencyP99Ms = latencyStats.P99Ms,
                LatencyMaxMs = latencyStats.MaxMs,
                TotalAllocatedBytes = memDelta.AllocatedBytes,
                AllocatedBytesPerMessage = receivedCount > 0 ? (double)memDelta.AllocatedBytes / receivedCount : 0,
                Gen0Collections = memDelta.Gen0,
                Gen1Collections = memDelta.Gen1,
                Gen2Collections = memDelta.Gen2,
                PeakWorkingSetMb = workingSetMb,
                ErrorCount = 0
            };
        }
        finally
        {
            await driver.DisposeAsync();
        }
    }
}
