namespace Centra.State;

public interface IStateStoreWriter
{
    ValueTask SetAsync<T>(
        string storeName,
        string key,
        T value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TrySetAsync<T>(
        string storeName,
        string key,
        T value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);
}
