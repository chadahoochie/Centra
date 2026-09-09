using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Standard implementation for formatting actor state keys.
/// </summary>
public sealed class ActorStateKeyFormatter : IActorStateKeyFormatter
{
    /// <summary>
    /// Singleton default instance of <see cref="ActorStateKeyFormatter"/>.
    /// </summary>
    public static readonly ActorStateKeyFormatter Instance = new();

    public string FormatStateKey(ActorIdentity identity, string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        return $"actors:{identity.Type.Value}:{identity.Id.Value}:{stateName}";
    }
}
