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

    public ValueTask<IDistributedLock> AcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        DistributedLockHelper.AcquireLockAsync(this, lockStoreName, resourceId, expiryTime, timeout, cancellationToken);

    private string BuildKey(string lockStoreName, string resourceId) =>
        $"{_options.KeyPrefix}lock:{lockStoreName}:{resourceId}";
}
