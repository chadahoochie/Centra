using RabbitMQ.Benchmark.Contracts;

namespace Centra.Benchmarks.RabbitMQ;

public static class CentraCommandLineParser
{
    public static CentraBenchmarkArguments Parse(string[] args)
    {
        var result = new CentraBenchmarkArguments();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                result.Scenario = args[++i].ToLowerInvariant();
            }
            else if (arg.Equals("--rabbitmq-uri", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
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
                result.QueueType = qt == "quorum" ? BenchmarkQueueType.Quorum : BenchmarkQueueType.Classic;
            }
            else if (arg.Equals("--output-json", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                result.OutputJsonPath = args[++i];
            }
            else if (arg.Equals("--micro", StringComparison.OrdinalIgnoreCase))
            {
                result.RunMicroBenchmarks = true;
            }
        }

        return result;
    }
}
