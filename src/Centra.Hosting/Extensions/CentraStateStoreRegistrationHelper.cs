using Centra.Hosting.Options;
using Centra.State;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

internal sealed class CentraStateStoreRegistrationHelper<T> : IStateStore<T>
{
    private readonly CentraStateStore<T> _inner;

    public CentraStateStoreRegistrationHelper(IStateStore stateStore, IOptions<CentraOptions> options)
    {
        var storeName = options.Value.DefaultStateStore;
        _inner = new CentraStateStore<T>(stateStore, storeName);
    }

    public ValueTask<StateEntry<T>?> GetAsync(string key, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(key, options, cancellationToken);

    public ValueTask SetAsync(string key, T value, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.SetAsync(key, value, options, cancellationToken);

    public ValueTask<bool> TrySetAsync(string key, T value, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.TrySetAsync(key, value, expectedETag, options, cancellationToken);

    public ValueTask DeleteAsync(string key, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(key, options, cancellationToken);

    public ValueTask<bool> TryDeleteAsync(string key, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.TryDeleteAsync(key, expectedETag, options, cancellationToken);
}
