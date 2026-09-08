using Centra.Actors;

namespace Centra.Sample.DockerStack.Domain;

/// <summary>
/// Virtual actor contract for a simple per-id counter, used to prove actor placement is spread across cluster nodes.
/// </summary>
public interface ICounterActor : IActor
{
    ValueTask<long> GetValueAsync();
    ValueTask<long> IncrementAsync();
}
