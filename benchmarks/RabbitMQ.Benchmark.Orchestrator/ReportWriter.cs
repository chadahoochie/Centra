using System.Text;
using RabbitMQ.Benchmark.Contracts;
using Spectre.Console;

namespace RabbitMQ.Benchmark.Orchestrator;

public static class ReportWriter
{
    public static void RenderConsoleTable(IReadOnlyList<ComparisonPair> pairs)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.Title("[bold yellow]Centra vs. Package.Queueing (VaxCare) — Benchmark Results[/]");

        table.AddColumn(new TableColumn("[bold]Scenario[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]Payload[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Conc[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold green]Centra Thpt[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold cyan]Pkg Thpt[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold gold1]Speedup[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold green]Centra P95[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold cyan]Pkg P95[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold green]Centra Alloc[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold cyan]Pkg Alloc[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold magenta]Alloc Savings[/]").RightAligned());

        foreach (var p in pairs)
        {
            table.AddRow(
                $"{p.Centra.Scenario} ({p.Centra.QueueType})",
                $"{p.Centra.PayloadSizeBytes}B",
                p.Centra.Concurrency.ToString(),
                $"{p.Centra.ThroughputMsgPerSec:N0} msg/s",
                $"{p.PackageQueueing.ThroughputMsgPerSec:N0} msg/s",
                $"[bold green]{p.ThroughputMultiplier:F2}x[/]",
                $"{p.Centra.LatencyP95Ms:F2}ms",
                $"{p.PackageQueueing.LatencyP95Ms:F2}ms",
                $"{p.Centra.AllocatedBytesPerMessage:N0} B",
                $"{p.PackageQueueing.AllocatedBytesPerMessage:N0} B",
                $"[bold green]{p.AllocationReductionPct:F1}%[/]"
            );
        }

        AnsiConsole.Write(table);
    }

    public static async Task GenerateMarkdownReportAsync(
        string outputPath,
        IReadOnlyList<ComparisonPair> pairs,
        string rabbitmqEndpoint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Realistic RabbitMQ Benchmark Report: Centra vs. Package.Queueing");
        sb.AppendLine();
        sb.AppendLine($"> Generated on **{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC** against `{rabbitmqEndpoint}`.");
        sb.AppendLine();
        sb.AppendLine("## 📊 Executive Summary");
        sb.AppendLine();
        sb.AppendLine("This benchmark performs an apples-to-apples performance comparison between:");
        sb.AppendLine("1. **Centra Framework (`Centra.Providers.RabbitMQ`)**: Native .NET 10 distributed application framework built on `RabbitMQ.Client 7.2.2`, CNCF CloudEvents v1.0 binary mode, zero-allocation memory pooling (`ReadOnlyMemory<byte>`, `ArrayPool<byte>.Shared`, `PooledDeliveryBody`), and pure `ValueTask` async execution.");
        sb.AppendLine("2. **Package.Queueing (`VaxCare.Package.Queueing`)**: Production queueing library built on `RabbitMQ.Client 6.8.1`, `VaxCare.Pipelining` Steppers, synchronous channel leasing, and custom wire envelope framing.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 📈 Performance Comparison Matrix");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Queue Type | Payload | Conc | Centra Throughput | Package.Queueing Throughput | Speedup Multiplier | Centra P95 Latency | Pkg P95 Latency | Centra Alloc/Msg | Pkg Alloc/Msg | Alloc Reduction |");
        sb.AppendLine("| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var p in pairs)
        {
            sb.AppendLine($"| **{p.Centra.Scenario}** | {p.Centra.QueueType} | {p.Centra.PayloadSizeBytes} B | {p.Centra.Concurrency} | **{p.Centra.ThroughputMsgPerSec:N0} msg/s** | {p.PackageQueueing.ThroughputMsgPerSec:N0} msg/s | **{p.ThroughputMultiplier:F2}x** | **{p.Centra.LatencyP95Ms:F2} ms** | {p.PackageQueueing.LatencyP95Ms:F2} ms | **{p.Centra.AllocatedBytesPerMessage:N0} B** | {p.PackageQueueing.AllocatedBytesPerMessage:N0} B | **{p.AllocationReductionPct:F1}%** |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 🏛️ Architectural Analysis & Drivers of Performance");
        sb.AppendLine();
        sb.AppendLine("### 1. Zero-Allocation Pipeline vs. Multi-Copy Boxing");
        sb.AppendLine("- **Centra**: Operates with `ReadOnlyMemory<byte>` and pooled delivery buffers (`PooledDeliveryBody`). Deserialization decodes directly from memory buffers without allocating intermediate `byte[]` arrays. In CloudEvents binary mode, payload bytes pass directly to the socket.");
        sb.AppendLine("- **Package.Queueing**: Calls `e.Body.ToArray()`, followed by LINQ `.Take(16).ToArray()` and `.Skip(16).ToArray()` in `DeserializeQueueMessage` to unpack the 16-byte Message GUID, allocating multiple arrays per message, wrapped in `Tuple<byte[], bool>` and `Stepper` delegates.");
        sb.AppendLine();
        sb.AppendLine("### 2. Modern Async Socket I/O vs. Synchronous Channel Locking");
        sb.AppendLine("- **Centra**: Uses `RabbitMQ.Client 7.2.2`'s non-blocking `BasicPublishAsync` and `AsyncEventingBasicConsumer`. Sockets are serviced asynchronously without lock contention.");
        sb.AppendLine("- **Package.Queueing**: Uses `RabbitMQ.Client 6.8.1` where channels require synchronization via `lock (leasedModel) { leasedModel.BasicPublish(...); }` and `lock (_channel) { _channel.BasicAck(...); }`. Under high concurrency, threads experience lock convoying.");
        sb.AppendLine();
        sb.AppendLine("### 3. Standards Compliance & Tracing Overhead");
        sb.AppendLine("- **Centra**: Adheres to CNCF CloudEvents v1.0 and W3C TraceContext standards using `System.Diagnostics.ActivitySource`. Distributed context is propagated via `traceparent` headers with zero heap allocations when listeners are quiescent.");
        sb.AppendLine("- **Package.Queueing**: Allocates custom `TelemetryContext` and `Activity` instances for every message dispatched through `Pipeline.ProcessQueueBytes`.");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }
}
