using System.Diagnostics;
using System.Text.Json;
using RabbitMQ.Benchmark.Contracts;

namespace RabbitMQ.Benchmark.Orchestrator;

public static class ProcessMetricCollector
{
    public static async Task<BenchmarkScenarioResult?> ExecuteSubprocessAsync(
        string projectPath,
        string scenario,
        Uri rabbitUri,
        int messages,
        int concurrency,
        int payloadSize,
        bool publisherConfirms,
        BenchmarkQueueType queueType,
        string tempJsonPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" --no-build -- " +
                        $"--scenario {scenario} " +
                        $"--rabbitmq-uri \"{rabbitUri}\" " +
                        $"--messages {messages} " +
                        $"--concurrency {concurrency} " +
                        $"--payload-size {payloadSize} " +
                        $"--confirms {publisherConfirms} " +
                        $"--queue-type {queueType.ToString().ToLowerInvariant()} " +
                        $"--output-json \"{tempJsonPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.WriteLine($"  [{Path.GetFileNameWithoutExtension(projectPath)}] {e.Data}");
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.Error.WriteLine($"  [ERR] {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Subprocess '{projectPath}' failed with exit code {process.ExitCode}");
            return null;
        }

        if (!File.Exists(tempJsonPath))
        {
            Console.Error.WriteLine($"Subprocess output JSON not found at '{tempJsonPath}'");
            return null;
        }

        var jsonContent = await File.ReadAllTextAsync(tempJsonPath);
        return JsonSerializer.Deserialize<BenchmarkScenarioResult>(jsonContent);
    }
}
