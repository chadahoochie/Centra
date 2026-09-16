using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public readonly record struct TenantOffloadWorkItem(
    string TenantId,
    string PubSubName,
    string Topic,
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, string> Headers,
    Func<CancellationToken, ValueTask<EventHandlingResult>> HandlerInvoker,
    DateTimeOffset CreatedAt,
    TaskCompletionSource<EventHandlingResult>? CompletionSource = null,
    Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>? DynamicInvoker = null);

