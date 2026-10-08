using RabbitMQ.Benchmark.Contracts;

namespace RabbitMQ.Benchmark.Orchestrator;

public static class OrchestratorCommandLineParser
{
    public static OrchestratorArguments Parse(string[] args)
    {
        var result = new OrchestratorArguments();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--rabbitmq-uri", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                result.RabbitUri = new Uri(args[++i]);
            }
            else if (arg.Equals("--messages", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var m)) result.Messages = m;
            }
            else if (arg.Equals("--concurrency", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var c)) result.Concurrency = c;
            }
            else if (arg.Equals("--payload-size", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var p)) result.PayloadSize = p;
            }
            else if (arg.Equals("--confirms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (bool.TryParse(args[++i], out var conf)) result.PublisherConfirms = conf;
            }
            else if (arg.Equals("--queue-type", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var qt = args[++i].ToLowerInvariant();
                if (qt is "both" or "all")
                {
                    result.QueueTypes = [BenchmarkQueueType.Classic, BenchmarkQueueType.Quorum];
                }
                else if (qt == "quorum")
                {
                    result.QueueTypes = [BenchmarkQueueType.Quorum];
                }
                else
                {
                    result.QueueTypes = [BenchmarkQueueType.Classic];
                }
            }
            else if (arg.Equals("--suite", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                result.Suite = args[++i].ToLowerInvariant();
            }
            else if (arg.Equals("--output-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                result.OutputDir = args[++i];
            }
        }

        return result;
    }
}
