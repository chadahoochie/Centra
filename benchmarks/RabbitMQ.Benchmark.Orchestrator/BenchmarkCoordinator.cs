using RabbitMQ.Benchmark.Contracts;
using Spectre.Console;
using Testcontainers.RabbitMq;

namespace RabbitMQ.Benchmark.Orchestrator;

public static class BenchmarkCoordinator
{
    public static async Task RunAsync(OrchestratorArguments args)
    {
        RabbitMqContainer? container = null;
        Uri rabbitUri;

        ConfigureDockerEnvironment();

        if (args.RabbitUri is not null)
        {
            rabbitUri = args.RabbitUri;
            AnsiConsole.MarkupLine($"[green]Using external RabbitMQ broker:[/] [yellow]{rabbitUri}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[bold blue]Starting RabbitMQ 4.3 container via Testcontainers...[/]");
            container = new RabbitMqBuilder("rabbitmq:4.3-management").Build();
            await container.StartAsync();
            rabbitUri = new Uri(container.GetConnectionString());
            AnsiConsole.MarkupLine($"[green]RabbitMQ container healthy:[/] [yellow]{rabbitUri}[/]");
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var repoRoot = FindRepoRoot(baseDir);

            var centraProject = Path.Combine(repoRoot, "benchmarks", "Centra.Benchmarks.RabbitMQ", "Centra.Benchmarks.RabbitMQ.csproj");
            var pkgProject = Path.Combine(repoRoot, "benchmarks", "PackageQueueing.Benchmarks.RabbitMQ", "PackageQueueing.Benchmarks.RabbitMQ.csproj");

            var tempDir = Path.Combine(Path.GetTempPath(), "rmq_benchmarks_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var scenariosToRun = new List<string>();
            if (args.Suite.Equals("producer", StringComparison.OrdinalIgnoreCase))
            {
                scenariosToRun.Add("producer");
            }
            else if (args.Suite.Equals("consumerdrain", StringComparison.OrdinalIgnoreCase))
            {
                scenariosToRun.Add("consumerdrain");
            }
            else if (args.Suite.Equals("endtoend", StringComparison.OrdinalIgnoreCase) || args.Suite.Equals("e2e", StringComparison.OrdinalIgnoreCase))
            {
                scenariosToRun.Add("endtoend");
            }
            else
            {
                scenariosToRun.Add("producer");
                scenariosToRun.Add("consumerdrain");
                scenariosToRun.Add("endtoend");
            }

            var pairs = new List<ComparisonPair>();

            foreach (var queueType in args.QueueTypes)
            {
                foreach (var scenario in scenariosToRun)
                {
                    AnsiConsole.MarkupLine($"\n[bold yellow]=== Executing Scenario: {scenario.ToUpperInvariant()} (Queue: {queueType}, Messages: {args.Messages}, Conc: {args.Concurrency}) ===[/]");

                    var suffix = $"{scenario}_{queueType.ToString().ToLowerInvariant()}";
                    var centraJson = Path.Combine(tempDir, $"centra_{suffix}.json");
                    var pkgJson = Path.Combine(tempDir, $"pkg_{suffix}.json");

                    AnsiConsole.MarkupLine("[bold cyan]>>> Running Centra Benchmark Harness...[/]");
                    var centraResult = await ProcessMetricCollector.ExecuteSubprocessAsync(
                        centraProject,
                        scenario,
                        rabbitUri,
                        args.Messages,
                        args.Concurrency,
                        args.PayloadSize,
                        args.PublisherConfirms,
                        queueType,
                        centraJson);

                    AnsiConsole.MarkupLine("[bold magenta]>>> Running Package.Queueing Benchmark Harness...[/]");
                    var pkgResult = await ProcessMetricCollector.ExecuteSubprocessAsync(
                        pkgProject,
                        scenario,
                        rabbitUri,
                        args.Messages,
                        args.Concurrency,
                        args.PayloadSize,
                        args.PublisherConfirms,
                        queueType,
                        pkgJson);

                    if (centraResult is not null && pkgResult is not null)
                    {
                        pairs.Add(new ComparisonPair
                        {
                            Centra = centraResult,
                            PackageQueueing = pkgResult
                        });
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[bold red]One of the benchmark harnesses failed to produce valid results.[/]");
                    }
                }
            }

            if (pairs.Count > 0)
            {
                AnsiConsole.WriteLine();
                ReportWriter.RenderConsoleTable(pairs);

                var outputPath = Path.Combine(args.OutputDir, "BENCHMARK_RESULTS.md");
                await ReportWriter.GenerateMarkdownReportAsync(outputPath, pairs, rabbitUri.ToString());
                AnsiConsole.MarkupLine($"\n[bold green]Detailed Markdown report written to:[/] [underline yellow]{outputPath}[/]");
            }
        }
        finally
        {
            if (container is not null)
            {
                AnsiConsole.MarkupLine("[grey]Disposing RabbitMQ container...[/]");
                await container.DisposeAsync();
            }
        }
    }

    public static string FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Centra.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Fallback to current working directory
        return Directory.GetCurrentDirectory();
    }

    public static void ConfigureDockerEnvironment()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var candidate = !string.IsNullOrEmpty(xdg)
                ? Path.Combine(xdg, "podman", "podman.sock")
                : "/run/user/1000/podman/podman.sock";

            if (File.Exists(candidate))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{candidate}");
                Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
            }
        }
    }
}
