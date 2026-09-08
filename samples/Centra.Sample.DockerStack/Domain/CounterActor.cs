using Centra.Actors;

namespace Centra.Sample.DockerStack.Domain;

public sealed class CounterActor : Actor, ICounterActor
{
    private const string CounterKey = "counter_value";

    public async ValueTask<long> GetValueAsync()
    {
        return await StateManager.GetStateAsync<long>(CounterKey).ConfigureAwait(false);
    }

    public async ValueTask<long> IncrementAsync()
    {
        var current = await StateManager.GetStateAsync<long>(CounterKey).ConfigureAwait(false);
        var updated = current + 1;
        await StateManager.SetStateAsync(CounterKey, updated).ConfigureAwait(false);
        return updated;
    }
}
