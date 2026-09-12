using Centra.State;

namespace Centra.ControlPlane.Catalog;

internal static class StateStoreRevisionCounter
{
    public static async ValueTask<long> IncrementRevisionAsync(
        IStateStore stateStore,
        string storeName,
        string revisionKey,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 10;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var entry = await stateStore.GetAsync<long>(storeName, revisionKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            var nextRev = (entry.HasValue ? entry.Value.Value : 0) + 1;
            var etag = entry?.ETag ?? string.Empty;

            var success = await stateStore.TrySetAsync(storeName, revisionKey, nextRev, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                return nextRev;
            }
        }

        var fallbackRev = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        await stateStore.SetAsync(storeName, revisionKey, fallbackRev, cancellationToken: cancellationToken).ConfigureAwait(false);
        return fallbackRev;
    }
}
