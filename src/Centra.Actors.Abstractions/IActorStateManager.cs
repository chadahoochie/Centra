namespace Centra.Actors;

/// <summary>
/// Manages persistent actor state with in-memory caching, dirty tracking, and optimistic concurrency.
/// </summary>
public interface IActorStateManager
{
    ValueTask<TState?> GetStateAsync<TState>(string stateName, CancellationToken cancellationToken = default);
    ValueTask SetStateAsync<TState>(string stateName, TState value, CancellationToken cancellationToken = default);
    ValueTask<bool> TrySetStateAsync<TState>(string stateName, TState value, CancellationToken cancellationToken = default);
    ValueTask<bool> RemoveStateAsync(string stateName, CancellationToken cancellationToken = default);
    ValueTask<bool> ContainsStateAsync(string stateName, CancellationToken cancellationToken = default);
    ValueTask SaveStateAsync(CancellationToken cancellationToken = default);
    ValueTask ClearCacheAsync();
}
