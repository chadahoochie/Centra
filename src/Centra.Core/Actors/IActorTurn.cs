namespace Centra.Core.Actors;

internal interface IActorTurn
{
    CancellationToken CancellationToken { get; }
    ValueTask ExecuteAsync();
    void SetCanceled();
}
