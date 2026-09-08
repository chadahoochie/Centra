using System.Net;
using Centra.Locks;
using Centra.Providers.CosmosDb.Documents;
using Microsoft.Azure.Cosmos;

namespace Centra.Providers.CosmosDb.Locks;

public sealed class CosmosDbDistributedLock : IDistributedLock
{
    private readonly Container _container;
    private string _currentETag;
    private int _disposed;

    public string LockStoreName { get; }
    public string ResourceId { get; }
    public string LockId { get; }

    public CosmosDbDistributedLock(
        Container container,
        string lockStoreName,
        string resourceId,
        string lockId,
        string initialETag)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        LockStoreName = lockStoreName ?? throw new ArgumentNullException(nameof(lockStoreName));
        ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        LockId = lockId ?? throw new ArgumentNullException(nameof(lockId));
        _currentETag = initialETag ?? throw new ArgumentNullException(nameof(initialETag));
    }

    public async ValueTask<bool> RenewAsync(TimeSpan newExpiryTime, CancellationToken cancellationToken = default)
    {
        if (_disposed != 0)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + newExpiryTime;
        var ttl = (int)Math.Max(1, newExpiryTime.TotalSeconds);

        var doc = new CosmosLockDocument
        {
            Id = ResourceId,
            LockStore = LockStoreName,
            ResourceId = ResourceId,
            LockId = LockId,
            AcquiredAtUtc = now,
            ExpiresAtUtc = expiresAt,
            TimeToLive = ttl
        };

        var requestOptions = new ItemRequestOptions
        {
            IfMatchEtag = _currentETag
        };

        try
        {
            var response = await _container.ReplaceItemAsync(
                doc,
                ResourceId,
                new PartitionKey(LockStoreName),
                requestOptions,
                cancellationToken).ConfigureAwait(false);

            _currentETag = response.ETag ?? _currentETag;
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed || ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var requestOptions = new ItemRequestOptions
        {
            IfMatchEtag = _currentETag
        };

        try
        {
            await _container.DeleteItemAsync<CosmosLockDocument>(
                ResourceId,
                new PartitionKey(LockStoreName),
                requestOptions).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Suppress exception during disposal (e.g. if the lock document already expired or was deleted)
            System.Diagnostics.Debug.WriteLine($"Failed to release Cosmos DB distributed lock: {ex.Message}");
        }
    }
}
