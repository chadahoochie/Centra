using Centra.PubSub.Inbox;
using Centra.State;

namespace Centra.Hosting.Inbox;

/// <summary>
/// Persistent implementation of <see cref="IInboxStore"/> backed by any configured <see cref="IStateStore"/>
/// with automatic TTL expiration.
/// </summary>
public sealed class StateStoreInboxStore : IInboxStore
{
    private readonly IStateStore _stateStore;
    private readonly string _storeName;

    public StateStoreInboxStore(IStateStore stateStore, string storeName = "statestore")
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _storeName = storeName;
    }

    public async ValueTask<bool> HasBeenProcessedAsync(string messageId, string consumerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);

        var key = $"centra:inbox:{consumerId}:{messageId}";
        var entry = await _stateStore.GetAsync<bool>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.HasValue;
    }

    public async ValueTask MarkProcessedAsync(string messageId, string consumerId, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);

        var key = $"centra:inbox:{consumerId}:{messageId}";
        var options = new StateOptions
        {
            TimeToLive = ttl ?? TimeSpan.FromDays(7)
        };

        await _stateStore.SetAsync(_storeName, key, true, options, cancellationToken).ConfigureAwait(false);
    }
}
