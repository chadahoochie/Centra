namespace Centra.Actors;

/// <summary>
/// Registration descriptor linking a virtual actor implementation to its domain interface.
/// </summary>
public sealed record ActorRegistration(Type ActorType, Type InterfaceType);
