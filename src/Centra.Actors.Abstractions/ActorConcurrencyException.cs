namespace Centra.Actors;

/// <summary>
/// Exception thrown when an optimistic concurrency conflict occurs while persisting actor state.
/// </summary>
public sealed class ActorConcurrencyException : Exception
{
    public ActorIdentity Identity { get; }
    public string StateName { get; }

    public ActorConcurrencyException(ActorIdentity identity, string stateName, string message)
        : base(message)
    {
        Identity = identity;
        StateName = stateName;
    }

    public ActorConcurrencyException(ActorIdentity identity, string stateName, string message, Exception innerException)
        : base(message, innerException)
    {
        Identity = identity;
        StateName = stateName;
    }
}
