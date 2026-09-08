using Microsoft.Extensions.Configuration;

namespace Centra.Sample.DockerStack.Simulator;

public sealed record SimulationOptions(
    IReadOnlyList<string> NodeBaseUrls,
    TimeSpan Interval,
    int ActorPoolSize)
{
    public static SimulationOptions FromConfiguration(IConfiguration configuration)
    {
        var nodeBaseUrls = (configuration["Simulation:NodeBaseUrls"]
                ?? "http://node-1:8080,http://node-2:8080,http://node-3:8080")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var intervalMs = int.TryParse(configuration["Simulation:IntervalMs"], out var parsedInterval)
            ? parsedInterval
            : 1500;

        var actorPoolSize = int.TryParse(configuration["Simulation:ActorPoolSize"], out var parsedPoolSize)
            ? parsedPoolSize
            : 10;

        return new SimulationOptions(nodeBaseUrls, TimeSpan.FromMilliseconds(intervalMs), actorPoolSize);
    }
}
