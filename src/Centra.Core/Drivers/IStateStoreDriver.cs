using Centra.State;

namespace Centra.Drivers;

public interface IStateStoreDriver
{
    ValueTask<StateEntry<byte[]>?> GetAsync(string storeName, string key, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string storeName, string key, ReadOnlyMemory<byte> value, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<bool> TrySetAsync(string storeName, string key, ReadOnlyMemory<byte> value, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string storeName, string key, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<bool> TryDeleteAsync(string storeName, string key, string expectedETag, StateOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask ExecuteTransactionAsync(string storeName, IReadOnlyList<StateTransactionOperation> operations, CancellationToken cancellationToken = default);
}
