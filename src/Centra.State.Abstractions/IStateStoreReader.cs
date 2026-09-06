namespace Centra.State;

public interface IStateStoreReader
{
    ValueTask<StateEntry<T>?> GetAsync<T>(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);
}
