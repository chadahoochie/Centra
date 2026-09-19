namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// One message's attempt history: how many redeliveries it has been charged, and when it was last charged.
/// The timestamp is what lets an attempt counter be reclaimed once the message can no longer come back.
/// </summary>
/// <param name="RetryNumber">Redeliveries charged so far, 1-based.</param>
/// <param name="LastChargedTicks">UTC tick count of the most recent charge.</param>
public readonly record struct RedeliveryAttemptState(int RetryNumber, long LastChargedTicks);
