namespace Centra.State;

public sealed record DeleteTransactionOperation(
    string Key,
    string? ExpectedETag = null) : StateTransactionOperation(Key);
