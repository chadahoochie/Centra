namespace Centra.State;

public interface IStateStore<T>
{
    ValueTask<StateEntry<T>?> GetAsync(string key, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string key, T value, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<bool> TrySetAsync(string key, T value, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string key, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<bool> TryDeleteAsync(string key, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default);
}
