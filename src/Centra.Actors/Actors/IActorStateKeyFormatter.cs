using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for formatting state persistence storage keys for virtual actors.
/// </summary>
public interface IActorStateKeyFormatter
{
    /// <summary>
    /// Formats the state persistence storage key for the given actor identity and state name.
    /// </summary>
    string FormatStateKey(ActorIdentity identity, string stateName);
}
