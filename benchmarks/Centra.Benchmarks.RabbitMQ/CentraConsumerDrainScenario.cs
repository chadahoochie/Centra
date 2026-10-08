using System.Diagnostics;
using Centra.PubSub;
using RabbitMQ.Benchmark.Contracts;
using RabbitMQ.Client;

namespace Centra.Benchmarks.RabbitMQ;

public static class CentraConsumerDrainScenario
{
    public static async Task<BenchmarkScenarioResult> RunAsync(
        Uri rabbitUri,
        int messageCount,
        int concurrency,
        int payloadSizeBytes,
        BenchmarkQueueType queueType)
    {
        var exchangeName = $"bench-centra-drain-{Guid.NewGuid():N}";
        var topic = "orders.drain";
        var queuePrefix = $"centra-drain-{Guid.NewGuid():N}";

        var (driver, client) = CentraBenchmarkDriverFactory.Create(
            rabbitUri,
            exchangeName,
            queuePrefix,
            queueType);

        try
        {
            var payloadBytes = new byte[payloadSizeBytes];
            Random.Shared.NextBytes(payloadBytes);

            // 1. Declare exchange, queue, and binding so messages accumulate before consumer connects
            var conn = await driver.GetConnectionAsync(CancellationToken.None);
            var setupChannel = await conn.CreateChannelAsync();
            await setupChannel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true);

            Dictionary<string, object?>? queueArgs = queueType == BenchmarkQueueType.Quorum
                ? new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }
                : null;

            var queueName = $"{queuePrefix}.pubsub.{topic}";
            await setupChannel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
            await setupChannel.QueueBindAsync(queueName, exchangeName, topic);

            // 2. Pre-seed messages into queue
            for (var i = 0; i < messageCount; i++)
            {
                var msg = new BenchmarkOrderMessage
                {
                    Id = Guid.NewGuid(),
                    CustomerId = $"cust-{i:D6}",
                    Amount = 10.00m,
                    SentTimestampTicks = Stopwatch.GetTimestamp(),
                    Payload = payloadBytes
                };

                await client.PublishAsync(topic, msg, options: new PubSubPublishOptions { Mode = Centra.Events.CloudEventMode.Binary });
            }

            var processedCount = 0;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memBefore = MemorySnapshot.Capture();
            var sw = Stopwatch.StartNew();

            // 3. Attach consumer and drain
            await driver.SubscribeAsync(
                "pubsub",
                topic,
                (body, headers, ct) =>
                {
                    var current = Interlocked.Increment(ref processedCount);
                    if (current >= messageCount)
                    {
                        tcs.TrySetResult();
                    }
                    return ValueTask.FromResult(EventHandlingResult.Success);
                },
                options: new PubSubSubscribeOptions
                {
                    MaxConcurrentCalls = concurrency,
                    PrefetchCount = concurrency * 30,
                    AutoDelete = false,
                    CustomArguments = queueArgs
                });

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            sw.Stop();

            var memAfter = MemorySnapshot.Capture();
            var memDelta = MemorySnapshot.ComputeDelta(memBefore, memAfter);

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var throughput = elapsedMs > 0 ? (processedCount / (elapsedMs / 1000.0)) : 0;
            var workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

            return new BenchmarkScenarioResult
            {
                Framework = "Centra",
                Scenario = "ConsumerDrain",
                QueueType = queueType.ToString(),
                Concurrency = concurrency,
                PayloadSizeBytes = payloadSizeBytes,
                PublisherConfirms = false,
                TotalMessages = processedCount,
                ElapsedMilliseconds = elapsedMs,
                ThroughputMsgPerSec = throughput,
                LatencyMeanMs = 0,
                LatencyP50Ms = 0,
                LatencyP90Ms = 0,
                LatencyP95Ms = 0,
                LatencyP99Ms = 0,
                LatencyMaxMs = 0,
                TotalAllocatedBytes = memDelta.AllocatedBytes,
                AllocatedBytesPerMessage = processedCount > 0 ? (double)memDelta.AllocatedBytes / processedCount : 0,
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
