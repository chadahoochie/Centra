using Centra.Providers.InMemory.Extensions;

namespace Centra;

/// <summary>
/// Extension methods for configuring in-memory providers on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraInMemoryBuilderExtensions
{
    /// <summary>
    /// Registers zero-dependency in-memory implementations for state store, pub/sub, locks, and bindings.
    /// </summary>
    public static ICentraBuilder AddInMemory(
        this ICentraBuilder builder,
        string defaultStateStore = "statestore",
        string defaultPubSub = "pubsub",
        string defaultLockStore = "lockstore")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraInMemory(defaultStateStore, defaultPubSub, defaultLockStore);
        return builder;
    }
}
