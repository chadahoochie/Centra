namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// The result of waiting for a subscription's in-flight handlers to finish.
/// <paramref name="Drained"/> records whether the wait itself ended because every handler
/// completed, so a clean drain can never be mistaken for a timeout because an unrelated
/// delivery started afterwards. <paramref name="Outstanding"/> is only meaningful when
/// <paramref name="Drained"/> is <see langword="false"/>.
/// </summary>
internal readonly record struct RabbitMQDrainOutcome(bool Drained, int Outstanding);
