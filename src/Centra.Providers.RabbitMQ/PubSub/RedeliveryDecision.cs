using Centra.PubSub;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// The outcome of charging a failed delivery against a <see cref="RedeliveryBudgetPolicy"/>: the settlement
/// to apply to the delivery, and how long to wait before applying it.
/// </summary>
/// <param name="Result">
/// <see cref="EventHandlingResult.Retry"/> while budget remains, otherwise <see cref="EventHandlingResult.DeadLetter"/>.
/// </param>
/// <param name="Delay">Backoff to observe before settling the delivery.</param>
public readonly record struct RedeliveryDecision(EventHandlingResult Result, TimeSpan Delay)
{
    /// <summary>
    /// The decision for a delivery that has no budget left, or whose identity cannot be established.
    /// </summary>
    public static readonly RedeliveryDecision DeadLetterImmediately = new(EventHandlingResult.DeadLetter, TimeSpan.Zero);
}
