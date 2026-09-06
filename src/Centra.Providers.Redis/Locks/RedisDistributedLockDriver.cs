using System.Diagnostics;
using Centra.Drivers;
using Centra.Locks;
using Centra.Providers.Redis.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Centra.Providers.Redis.Locks;

public sealed class RedisDistributedLockDriver : IDistributedLockDriver
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisProviderOptions _options;

    public RedisDistributedLockDriver(IConnectionMultiplexer connection, IOptions<RedisProviderOptions> options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? new RedisProviderOptions();
    }

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockStoreName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var db = _connection.GetDatabase();
        var redisKey = BuildKey(lockStoreName, resourceId);
        var lockId = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(
            redisKey,
            lockId,
            expiryTime,
            When.NotExists).ConfigureAwait(false);

        if (acquired)
        {
            return new RedisDistributedLock(db, redisKey, resourceId, lockId);
        }

        return null;
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

    private string BuildKey(string lockStoreName, string resourceId) =>
        $"{_options.KeyPrefix}lock:{lockStoreName}:{resourceId}";
}
