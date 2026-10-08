using System.Text.Json;
using BenchmarkDotNet.Running;
using PackageQueueing.Benchmarks.RabbitMQ;
using RabbitMQ.Benchmark.Contracts;

var options = PackageQueueingCommandLineParser.Parse(args);

if (options.RunMicroBenchmarks)
{
    Console.WriteLine("[Package.Queueing] Running BenchmarkDotNet microbenchmarks...");
    BenchmarkRunner.Run<PackageQueueingMicroBenchmarks>();
    return;
}

Console.WriteLine($"[Package.Queueing] Starting Scenario '{options.Scenario}' with {options.Messages} messages, concurrency {options.Concurrency}, payload {options.PayloadSize}B, confirms={options.PublisherConfirms}, queue={options.QueueType}...");

BenchmarkScenarioResult result;

switch (options.Scenario)
{
    case "consumerdrain":
        result = await PackageQueueingConsumerDrainScenario.RunAsync(
            options.RabbitUri,
            options.Messages,
            options.Concurrency,
            options.PayloadSize,
            options.QueueType);
        break;

    case "endtoend":
    case "e2e":
        result = await PackageQueueingEndToEndScenario.RunAsync(
            options.RabbitUri,
            options.Messages,
            options.Concurrency,
            options.PayloadSize,
            options.PublisherConfirms,
            options.QueueType);
        break;

    case "producer":
    default:
        result = await PackageQueueingProducerScenario.RunAsync(
            options.RabbitUri,
            options.Messages,
            options.Concurrency,
            options.PayloadSize,
            options.PublisherConfirms,
            options.QueueType);
        break;
}

Console.WriteLine($"[Package.Queueing] Completed '{result.Scenario}': {result.ThroughputMsgPerSec:N0} msg/s | P50={result.LatencyP50Ms:F2}ms | P95={result.LatencyP95Ms:F2}ms | Gen0={result.Gen0Collections} | Alloc={result.AllocatedBytesPerMessage:N0} B/msg");

if (!string.IsNullOrWhiteSpace(options.OutputJsonPath))
{
    var dir = Path.GetDirectoryName(options.OutputJsonPath);
    if (!string.IsNullOrWhiteSpace(dir))
    {
        Directory.CreateDirectory(dir);
    }

    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(options.OutputJsonPath, json);
    Console.WriteLine($"[Package.Queueing] Metrics saved to {options.OutputJsonPath}");
}
