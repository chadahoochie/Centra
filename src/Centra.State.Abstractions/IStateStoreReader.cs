namespace Centra.State;

public interface IStateStoreReader
{
    ValueTask<StateEntry<T>?> GetAsync<T>(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StateEntry<T>>> GetBatchAsync<T>(
        string storeName,
        IReadOnlyList<string> keys,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);
}
