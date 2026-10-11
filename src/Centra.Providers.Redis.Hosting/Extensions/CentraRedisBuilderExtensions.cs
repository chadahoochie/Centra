using Centra.Providers.Redis.Extensions;
using Centra.Providers.Redis.Options;

namespace Centra;

/// <summary>
/// Extension methods for configuring Redis providers on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraRedisBuilderExtensions
{
    /// <summary>
    /// Registers Redis implementations for state store, pub/sub, and distributed locks.
    /// </summary>
    public static ICentraBuilder AddRedis(
        this ICentraBuilder builder,
        Action<RedisProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraRedis(configure);
        return builder;
    }

    /// <summary>
    /// Registers a named Redis State Store component.
    /// </summary>
    public static ICentraBuilder AddRedisStateStore(
        this ICentraBuilder builder,
        string storeName,
        Action<RedisProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraRedisStateStore(storeName, configure);
        return builder;
    }

    /// <summary>
    /// Registers a named Redis Pub/Sub broker component.
    /// </summary>
    public static ICentraBuilder AddRedisPubSub(
        this ICentraBuilder builder,
        string pubSubName,
        Action<RedisProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraRedisPubSub(pubSubName, configure);
        return builder;
    }

    /// <summary>
    /// Registers a named Redis Distributed Lock store component.
    /// </summary>
    public static ICentraBuilder AddRedisLocks(
        this ICentraBuilder builder,
        string lockStoreName,
        Action<RedisProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraRedisLocks(lockStoreName, configure);
        return builder;
    }
}
