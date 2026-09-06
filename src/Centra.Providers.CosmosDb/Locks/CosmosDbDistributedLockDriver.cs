using System.Diagnostics;
using System.Net;
using Centra.Drivers;
using Centra.Locks;
using Centra.Providers.CosmosDb.Documents;
using Centra.Providers.CosmosDb.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Centra.Providers.CosmosDb.Locks;

public sealed class CosmosDbDistributedLockDriver : IDistributedLockDriver
{
    private readonly CosmosClient _client;
    private readonly CosmosDbProviderOptions _options;
    private int _initialized;

    public CosmosDbDistributedLockDriver(
        CosmosClient client,
        IOptions<CosmosDbProviderOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? new CosmosDbProviderOptions();
    }

    private Container LockContainer => _client.GetContainer(_options.DatabaseName, _options.LockContainerName);

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoCreateDatabaseAndContainers && Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            var dbResponse = await _client.CreateDatabaseIfNotExistsAsync(_options.DatabaseName, cancellationToken: cancellationToken).ConfigureAwait(false);
            var containerProperties = new ContainerProperties(_options.LockContainerName, _options.LockPartitionKeyPath)
            {
                DefaultTimeToLive = -1
            };
            await dbResponse.Database.CreateContainerIfNotExistsAsync(containerProperties, _options.Throughput, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockStoreName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var lockId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + expiryTime;
        var ttl = (int)Math.Max(1, expiryTime.TotalSeconds);

        var doc = new CosmosLockDocument
        {
            Id = resourceId,
            LockStore = lockStoreName,
            ResourceId = resourceId,
            LockId = lockId,
            AcquiredAtUtc = now,
            ExpiresAtUtc = expiresAt,
            TimeToLive = ttl
        };

        try
        {
            // Attempt optimistic creation if not exists
            var response = await LockContainer.CreateItemAsync(
                doc,
                new PartitionKey(lockStoreName),
                new ItemRequestOptions { IfNoneMatchEtag = "*" },
                cancellationToken).ConfigureAwait(false);

            return new CosmosDbDistributedLock(LockContainer, lockStoreName, resourceId, lockId, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Document exists: check if expired
            try
            {
                var existing = await LockContainer.ReadItemAsync<CosmosLockDocument>(
                    resourceId,
                    new PartitionKey(lockStoreName),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (existing.Resource is not null && DateTimeOffset.UtcNow > existing.Resource.ExpiresAtUtc)
                {
                    // Existing lock expired, replace conditionally using ETag
                    var replaceResponse = await LockContainer.ReplaceItemAsync(
                        doc,
                        resourceId,
                        new PartitionKey(lockStoreName),
                        new ItemRequestOptions { IfMatchEtag = existing.ETag },
                        cancellationToken).ConfigureAwait(false);

                    return new CosmosDbDistributedLock(LockContainer, lockStoreName, resourceId, lockId, replaceResponse.ETag);
                }
            }
            catch (CosmosException)
            {
                return null;
            }

            return null;
        }
    }

    public async ValueTask<IDistributedLock> AcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockStoreName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var startTimestamp = Stopwatch.GetTimestamp();

        while (!cancellationToken.IsCancellationRequested)
        {
            var @lock = await TryAcquireLockAsync(lockStoreName, resourceId, expiryTime, cancellationToken).ConfigureAwait(false);
            if (@lock is not null)
            {
                return @lock;
            }

            if (Stopwatch.GetElapsedTime(startTimestamp) >= timeout)
            {
                throw new TimeoutException($"Failed to acquire lock for resource '{resourceId}' in store '{lockStoreName}' within {timeout.TotalSeconds}s.");
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(cancellationToken);
    }
}
