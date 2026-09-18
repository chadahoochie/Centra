using Centra.Drivers;
using Centra.Providers.Redis.Options;
using Centra.State;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Centra.Providers.Redis.State;

public sealed class RedisStateStoreDriver : IStateStoreDriver
{
    private static readonly RedisValue DataField = "data";
    private static readonly RedisValue ETagField = "etag";

    private static readonly LuaScript PreparedTrySetScript = LuaScript.Prepare("""
        local currentEtag = redis.call('hget', @key, 'etag')
        if currentEtag == false or currentEtag == @expectedETag then
            redis.call('hset', @key, 'data', @value, 'etag', @newEtag)
            local ttlMs = tonumber(@ttlMs)
            if ttlMs and ttlMs > 0 then
                redis.call('pexpire', @key, ttlMs)
            end
            return 1
        else
            return 0
        end
        """);

    private static readonly LuaScript PreparedTryDeleteScript = LuaScript.Prepare("""
        local currentEtag = redis.call('hget', @key, 'etag')
        if currentEtag == @expectedETag then
            redis.call('del', @key)
            return 1
        else
            return 0
        end
        """);

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisProviderOptions _options;

    public RedisStateStoreDriver(IConnectionMultiplexer connection, IOptions<RedisProviderOptions> options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? new RedisProviderOptions();
    }

    public async ValueTask<StateEntry<byte[]>?> GetAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _connection.GetDatabase();
        var redisKey = BuildKey(storeName, key);

        var values = await db.HashGetAsync(redisKey, [DataField, ETagField]).ConfigureAwait(false);
        if (values[0].IsNullOrEmpty)
        {
            return null;
        }

        byte[] data = values[0]!;
        string etag = values[1].HasValue ? values[1].ToString() : string.Empty;

        return new StateEntry<byte[]>(key, data, etag, null);
    }

    public async ValueTask<IReadOnlyList<StateEntry<byte[]>>> GetBatchAsync(
        string storeName,
        IReadOnlyList<string> keys,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return Array.Empty<StateEntry<byte[]>>();
        }

        var db = _connection.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new Task<RedisValue[]>[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            var redisKey = BuildKey(storeName, keys[i]);
            tasks[i] = batch.HashGetAsync(redisKey, [DataField, ETagField]);
        }

        batch.Execute();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var list = new List<StateEntry<byte[]>>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            var values = results[i];
            if (!values[0].IsNullOrEmpty)
            {
                byte[] data = values[0]!;
                string etag = values[1].HasValue ? values[1].ToString() : string.Empty;
                list.Add(new StateEntry<byte[]>(keys[i], data, etag, null));
            }
        }

        return list;
    }

    public async ValueTask SetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _connection.GetDatabase();
        var redisKey = BuildKey(storeName, key);
        var newEtag = Guid.NewGuid().ToString("N");

        await db.HashSetAsync(
            redisKey,
            [new HashEntry(DataField, value), new HashEntry(ETagField, newEtag)]).ConfigureAwait(false);

        if (options?.TimeToLive is not null && options.TimeToLive > TimeSpan.Zero)
        {
            await db.KeyExpireAsync(redisKey, options.TimeToLive).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> TrySetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        var db = _connection.GetDatabase();
        var redisKey = BuildKey(storeName, key);
        var newEtag = Guid.NewGuid().ToString("N");
        var ttlMs = (options?.TimeToLive is not null && options.TimeToLive > TimeSpan.Zero)
            ? (long)options.TimeToLive.Value.TotalMilliseconds
            : 0;

        var result = await PreparedTrySetScript.EvaluateAsync(
            db,
            new
            {
                key = (RedisKey)redisKey,
                expectedETag = (RedisValue)expectedETag,
                value = (RedisValue)value,
                newEtag = (RedisValue)newEtag,
                ttlMs = (RedisValue)ttlMs
            }).ConfigureAwait(false);

        return (long)result == 1;
    }

    public async ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _connection.GetDatabase();
        var redisKey = BuildKey(storeName, key);

        await db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        var db = _connection.GetDatabase();
        var redisKey = BuildKey(storeName, key);

        var result = await PreparedTryDeleteScript.EvaluateAsync(
            db,
            new
            {
                key = (RedisKey)redisKey,
                expectedETag = (RedisValue)expectedETag
            }).ConfigureAwait(false);

        return (long)result == 1;
    }

    public async ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            return;
        }

        var db = _connection.GetDatabase();
        var tx = db.CreateTransaction();

        foreach (var op in operations)
        {
            var opKey = BuildKey(storeName, op.Key);
            switch (op)
            {
                case SetTransactionOperation<byte[]> setOp:
                    var newEtag = Guid.NewGuid().ToString("N");
                    _ = tx.HashSetAsync(
                        opKey,
                        [new HashEntry(DataField, setOp.Value), new HashEntry(ETagField, newEtag)]);
                    if (setOp.Options?.TimeToLive is not null && setOp.Options.TimeToLive > TimeSpan.Zero)
                    {
                        _ = tx.KeyExpireAsync(opKey, setOp.Options.TimeToLive);
                    }
                    break;

                case DeleteTransactionOperation:
                    _ = tx.KeyDeleteAsync(opKey);
                    break;
            }
        }

        var committed = await tx.ExecuteAsync().ConfigureAwait(false);
        if (!committed)
        {
            throw new InvalidOperationException("Redis transaction execution failed to commit.");
        }
    }

    internal string BuildKey(string storeName, string key) =>
        $"{_options.KeyPrefix}state:{storeName}:{key}";
}
