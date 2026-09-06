namespace Centra.State;

public sealed record SetTransactionOperation<T>(
    string Key,
    T Value,
    string? ExpectedETag = null,
    StateOptions? Options = null) : StateTransactionOperation(Key);
