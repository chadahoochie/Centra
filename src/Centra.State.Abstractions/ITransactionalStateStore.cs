namespace Centra.State;

public interface ITransactionalStateStore
{
    ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default);
}
