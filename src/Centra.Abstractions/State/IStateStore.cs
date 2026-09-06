namespace Centra.State;

public interface IStateStore : IStateStoreReader, IStateStoreWriter, ITransactionalStateStore
{
}
