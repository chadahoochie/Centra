using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for persisting actor state mutations with optimistic concurrency control.
/// </summary>
internal interface IActorStatePersister
{
    /// <summary>
    /// Persists an individual actor state entry mutation to the underlying state store.
    /// </summary>
    ValueTask PersistEntryAsync(
        ActorIdentity identity,
        string storeName,
        string key,
        string stateName,
        ActorStateEntry entry,
        CancellationToken cancellationToken);
}
