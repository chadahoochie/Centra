using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Benchmark.Contracts;
using VaxCare.Queueing;
using VaxCare.Queueing.Core;
using VaxCare.Queueing.Core.Configuration;
using VaxCare.Queueing.Definition;

namespace PackageQueueing.Benchmarks.RabbitMQ;

public static class PackageQueueingConsumerDrainScenario
{
    public static async Task<BenchmarkScenarioResult> RunAsync(
        Uri rabbitUri,
        int messageCount,
        int concurrency,
        int payloadSizeBytes,
        BenchmarkQueueType queueType)
    {
        var exchangeName = $"bench-pkg-drain-{Guid.NewGuid():N}";
        var routingKey = "orders.pkg.drain";
        var queueName = $"pkg-drain-queue-{Guid.NewGuid():N}";

        var payloadBytes = new byte[payloadSizeBytes];
        Random.Shared.NextBytes(payloadBytes);

        // Pre-declare exchange and queue with binding so seeded messages are routed to the queue
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

        var seedServices = new ServiceCollection();
        seedServices.AddVaxCareQueueing(cfg =>
        {
            cfg.Uri = rabbitUri;
            cfg.RabbitMqConnectionLabel = "pkg-bench-seeder";
        }, q =>
        {
            q.AddEnqueuer<BenchmarkOrderMessage>(exchangeName, routingKey);
        });

        using (var seedSp = seedServices.BuildServiceProvider())
        {
            var enqueuer = seedSp.GetRequiredService<IEnqueuer<BenchmarkOrderMessage>>();
            for (var i = 0; i < messageCount; i++)
            {
                enqueuer.Enqueue(new BenchmarkOrderMessage
                {
                    Id = Guid.NewGuid(),
                    CustomerId = $"cust-{i:D6}",
                    Amount = 10.00m,
                    SentTimestampTicks = Stopwatch.GetTimestamp(),
                    Payload = payloadBytes
                });
            }
        }

        var processedCount = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddVaxCareQueueing(cfg =>
                {
                    cfg.Uri = rabbitUri;
                    cfg.RabbitMqConnectionLabel = "pkg-bench-consumer";
                    cfg.Concurrency.DefaultMaxConcurrency = (ushort)concurrency;
                }, q =>
                {
                    q.AddQueueClient<BenchmarkOrderMessage, ProcessedResult>(
                        (msg, sp) =>
                        {
                            var current = Interlocked.Increment(ref processedCount);
                            if (current >= messageCount)
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

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memBefore = MemorySnapshot.Capture();
        using var host = hostBuilder.Build();
        var sw = Stopwatch.StartNew();
        await host.StartAsync();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
        sw.Stop();
        await host.StopAsync();

        var memAfter = MemorySnapshot.Capture();
        var memDelta = MemorySnapshot.ComputeDelta(memBefore, memAfter);

        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        var throughput = elapsedMs > 0 ? (processedCount / (elapsedMs / 1000.0)) : 0;
        var workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

        return new BenchmarkScenarioResult
        {
            Framework = "Package.Queueing",
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
}
