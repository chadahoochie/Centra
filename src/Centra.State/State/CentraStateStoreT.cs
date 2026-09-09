using Centra.State;

namespace Centra.State;

public sealed class CentraStateStore<T> : IStateStore<T>
{
    private readonly IStateStore _innerStore;
    private readonly string _storeName;

    public CentraStateStore(IStateStore innerStore, string storeName)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _storeName = storeName ?? throw new ArgumentNullException(nameof(storeName));
    }

    public ValueTask<StateEntry<T>?> GetAsync(string key, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _innerStore.GetAsync<T>(_storeName, key, options, cancellationToken);

    public ValueTask SetAsync(string key, T value, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _innerStore.SetAsync<T>(_storeName, key, value, options, cancellationToken);

    public ValueTask<bool> TrySetAsync(string key, T value, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _innerStore.TrySetAsync<T>(_storeName, key, value, expectedETag, options, cancellationToken);

    public ValueTask DeleteAsync(string key, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _innerStore.DeleteAsync(_storeName, key, options, cancellationToken);

    public ValueTask<bool> TryDeleteAsync(string key, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _innerStore.TryDeleteAsync(_storeName, key, expectedETag, options, cancellationToken);
}
