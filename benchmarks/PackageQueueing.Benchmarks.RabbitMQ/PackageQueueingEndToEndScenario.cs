using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Benchmark.Contracts;
using VaxCare.Queueing;
using VaxCare.Queueing.Core;
using VaxCare.Queueing.Core.Configuration;
using VaxCare.Queueing.Definition;

namespace PackageQueueing.Benchmarks.RabbitMQ;

public static class PackageQueueingEndToEndScenario
{
    public static async Task<BenchmarkScenarioResult> RunAsync(
        Uri rabbitUri,
        int messageCount,
        int concurrency,
        int payloadSizeBytes,
        bool publisherConfirms,
        BenchmarkQueueType queueType)
    {
        var exchangeName = $"bench-pkg-e2e-{Guid.NewGuid():N}";
        var routingKey = "orders.pkg.e2e";
        var queueName = $"pkg-e2e-queue-{Guid.NewGuid():N}";

        var payloadBytes = new byte[payloadSizeBytes];
        Random.Shared.NextBytes(payloadBytes);

        // Pre-declare exchange and queue with binding so messages aren't dropped before background consumer initializes
        var factory = new global::RabbitMQ.Client.ConnectionFactory { Uri = rabbitUri };
        using (var setupConn = factory.CreateConnection())
        using (var setupModel = setupConn.CreateModel())
        {
            setupModel.ExchangeDeclare(exchangeName, global::RabbitMQ.Client.ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);
            IDictionary<string, object>? queueArgs = queueType == BenchmarkQueueType.Quorum
                ? new Dictionary<string, object> { ["x-queue-type"] = "quorum" }
                : null;
            setupModel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
            setupModel.QueueBind(queueName, exchangeName, $"#.{routingKey}.#", arguments: null);
        }

        var transitTimingsMs = new double[messageCount];
        var receivedCount = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddVaxCareQueueing(cfg =>
                {
                    cfg.Uri = rabbitUri;
                    cfg.RabbitMqConnectionLabel = "pkg-bench-e2e";
                    cfg.PublisherConfirmsEnabled = publisherConfirms;
                    cfg.Concurrency.DefaultMaxConcurrency = (ushort)concurrency;
                }, q =>
                {
                    q.AddEnqueuer<BenchmarkOrderMessage>(exchangeName, routingKey);

                    q.AddQueueClient<BenchmarkOrderMessage, ProcessedResult>(
                        (msg, sp) =>
                        {
                            var receiveTs = Stopwatch.GetTimestamp();
                            var index = Interlocked.Increment(ref receivedCount) - 1;

                            if (msg.SentTimestampTicks > 0)
                            {
                                var latency = Stopwatch.GetElapsedTime(msg.SentTimestampTicks, receiveTs).TotalMilliseconds;
                                if (index < transitTimingsMs.Length)
                                {
                                    transitTimingsMs[index] = latency;
                                }
                            }

                            if (receivedCount >= messageCount)
                            {
                                tcs.TrySetResult();
                            }
                            return new ProcessedResult();
                        },
                        new QueueClientConfiguration(
                            exchangeName: exchangeName,
                            queueName: queueName,
                            bindings: new List<string> { routingKey }),
                        queueType: queueType == BenchmarkQueueType.Quorum ? QueueType.Quorum : QueueType.Classic,
                        excludeFromTelemetry: true);
                });
            });

        using var host = hostBuilder.Build();
        await host.StartAsync();
        await Task.Delay(200);

        var enqueuer = host.Services.GetRequiredService<IEnqueuer<BenchmarkOrderMessage>>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memBefore = MemorySnapshot.Capture();
        var swTotal = Stopwatch.StartNew();

        var publishTask = Task.Run(() =>
        {
            Parallel.For(0, messageCount, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
            {
                var msg = new BenchmarkOrderMessage
                {
                    Id = Guid.NewGuid(),
                    CustomerId = $"cust-{i:D6}",
                    Amount = 25.50m,
                    SentTimestampTicks = Stopwatch.GetTimestamp(),
                    Payload = payloadBytes
                };

                enqueuer.Enqueue(msg);
            });
        });

        await publishTask;
        var timeoutSeconds = Math.Max(30, messageCount / 10);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (completed != tcs.Task)
        {
            Console.WriteLine($"[Package.Queueing] Warning: EndToEnd scenario timed out waiting for all messages. Received {receivedCount}/{messageCount}");
        }

        swTotal.Stop();
        await host.StopAsync();

        var memAfter = MemorySnapshot.Capture();
        var memDelta = MemorySnapshot.ComputeDelta(memBefore, memAfter);

        var elapsedMs = swTotal.Elapsed.TotalMilliseconds;
        var throughput = elapsedMs > 0 ? (receivedCount / (elapsedMs / 1000.0)) : 0;
        var latencyStats = LatencyCalculator.Calculate(transitTimingsMs[..receivedCount]);
        var workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

        return new BenchmarkScenarioResult
        {
            Framework = "Package.Queueing",
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
}
