using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Benchmark.Contracts;
using RabbitMQ.Client;
using VaxCare.Queueing;
using VaxCare.Queueing.Core;
using VaxCare.Queueing.Definition;

namespace PackageQueueing.Benchmarks.RabbitMQ;

public static class PackageQueueingProducerScenario
{
    public static async Task<BenchmarkScenarioResult> RunAsync(
        Uri rabbitUri,
        int messageCount,
        int concurrency,
        int payloadSizeBytes,
        bool publisherConfirms,
        BenchmarkQueueType queueType)
    {
        var exchangeName = $"bench-pkg-prod-{Guid.NewGuid():N}";
        var routingKey = "orders.pkg.bench";
        var queueName = $"pkg-prod-drain-{Guid.NewGuid():N}";

        // Pre-declare exchange and drain consumer so messages do not accumulate indefinitely
        var factory = new ConnectionFactory { Uri = rabbitUri };
        using var drainConn = factory.CreateConnection();
        using var drainModel = drainConn.CreateModel();
        drainModel.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true);

        IDictionary<string, object>? queueArgs = queueType == BenchmarkQueueType.Quorum
            ? new Dictionary<string, object> { ["x-queue-type"] = "quorum" }
            : null;

        drainModel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: queueType != BenchmarkQueueType.Quorum, arguments: queueArgs);
        drainModel.QueueBind(queueName, exchangeName, $"#.{routingKey}.#");

        var drainConsumer = new global::RabbitMQ.Client.Events.EventingBasicConsumer(drainModel);
        drainConsumer.Received += (_, _) => { };
        drainModel.BasicConsume(queueName, autoAck: true, consumer: drainConsumer);

        var services = new ServiceCollection();
        services.AddVaxCareQueueing(cfg =>
        {
            cfg.Uri = rabbitUri;
            cfg.RabbitMqConnectionLabel = "pkg-bench-producer";
            cfg.PublisherConfirmsEnabled = publisherConfirms;
        }, q =>
        {
            q.AddEnqueuer<BenchmarkOrderMessage>(exchangeName, routingKey);
        });

        using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<IEnqueuer<BenchmarkOrderMessage>>();

        var payloadBytes = new byte[payloadSizeBytes];
        Random.Shared.NextBytes(payloadBytes);

        var timingsMs = new double[messageCount];
        var errorCount = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memBefore = MemorySnapshot.Capture();
        var swTotal = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            Parallel.For(0, messageCount, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
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
                    var result = enqueuer.Enqueue(msg);
                    timingsMs[i] = Stopwatch.GetElapsedTime(swMsg).TotalMilliseconds;

                    if (!result.IsSuccess)
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                    timingsMs[i] = Stopwatch.GetElapsedTime(swMsg).TotalMilliseconds;
                }
            });
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
            Framework = "Package.Queueing",
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
}
