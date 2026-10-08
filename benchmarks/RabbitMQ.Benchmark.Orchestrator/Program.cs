using RabbitMQ.Benchmark.Orchestrator;
using Spectre.Console;

AnsiConsole.Write(new FigletText("RabbitMQ Benchmark").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold]Centra PubSub vs. Package.Queueing (VaxCare)[/]");
AnsiConsole.MarkupLine("[grey]Comparing async ValueTask zero-allocation pipelines against synchronous leased models.[/]\n");

var options = OrchestratorCommandLineParser.Parse(args);
await BenchmarkCoordinator.RunAsync(options);
