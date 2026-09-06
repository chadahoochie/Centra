using Centra.Locks;
using StackExchange.Redis;

namespace Centra.Providers.Redis.Locks;

public sealed class RedisDistributedLock : IDistributedLock
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    private const string RenewScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('pexpire', KEYS[1], ARGV[2])
        else
            return 0
        end
        """;

    private readonly IDatabase _database;
    private readonly string _redisKey;
    private int _disposed;

    public string ResourceId { get; }
    public string LockId { get; }

    public RedisDistributedLock(IDatabase database, string redisKey, string resourceId, string lockId)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _redisKey = redisKey ?? throw new ArgumentNullException(nameof(redisKey));
        ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        LockId = lockId ?? throw new ArgumentNullException(nameof(lockId));
    }

    public async ValueTask<bool> RenewAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default)
    {
        if (_disposed != 0)
        {
            return false;
        }

        var ttlMs = (long)additionalTime.TotalMilliseconds;
        var result = await _database.ScriptEvaluateAsync(
            RenewScript,
            [new RedisKey(_redisKey)],
            [LockId, ttlMs]).ConfigureAwait(false);

        return (long)result == 1;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _database.ScriptEvaluateAsync(
            ReleaseScript,
            [new RedisKey(_redisKey)],
            [LockId]).ConfigureAwait(false);
    }
}
