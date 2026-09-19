using Centra.Providers.RabbitMQ.Options;
using Centra.PubSub;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// The redelivery budget resolved for a single subscription: how many redeliveries a handler returning
/// <see cref="EventHandlingResult.Retry"/> is granted before the message is dead-lettered, and the
/// exponential backoff applied between them.
/// </summary>
/// <param name="MaxRetryAttempts">
/// Redeliveries granted before dead-lettering. A budget of 3 yields at most 4 handler invocations.
/// Zero or less disables the budget entirely, restoring unbounded redelivery.
/// </param>
/// <param name="InitialBackoff">Delay preceding the first redelivery. Non-positive disables backoff.</param>
/// <param name="MaxBackoff">Ceiling applied to the doubling backoff. Non-positive disables the ceiling.</param>
public readonly record struct RedeliveryBudgetPolicy(int MaxRetryAttempts, TimeSpan InitialBackoff, TimeSpan MaxBackoff)
{
    /// <summary>
    /// The largest doubling applied to <see cref="InitialBackoff"/>; beyond this the backoff has long since
    /// saturated <see cref="MaxBackoff"/> and further shifting would only risk overflowing the tick count.
    /// </summary>
    public const int MaxBackoffDoublings = 30;

    /// <summary>
    /// Whether this subscription has a redelivery budget at all. A non-positive <see cref="MaxRetryAttempts"/>
    /// is a deliberate opt-out: the handler's <see cref="EventHandlingResult.Retry"/> requeues forever, no
    /// delivery is ever dead-lettered for budget exhaustion, and nothing needs a dead-letter route.
    /// </summary>
    public bool IsEnabled => MaxRetryAttempts > 0;

    /// <summary>
    /// Resolves the effective policy for a subscription, preferring per-subscription overrides and falling
    /// back to the provider-wide defaults.
    /// </summary>
    public static RedeliveryBudgetPolicy Resolve(PubSubSubscribeOptions? options, RabbitMQProviderOptions providerOptions)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);

        return new RedeliveryBudgetPolicy(
            options?.MaxRetryAttempts ?? providerOptions.DefaultMaxRetryAttempts,
            options?.RetryInitialBackoff ?? providerOptions.DefaultRetryInitialBackoff,
            options?.RetryMaxBackoff ?? providerOptions.DefaultRetryMaxBackoff);
    }

    /// <summary>
    /// Computes the delay preceding the 1-based <paramref name="retryNumber"/>: <see cref="InitialBackoff"/>
    /// doubled once per prior retry, clamped to <see cref="MaxBackoff"/>.
    /// </summary>
    public TimeSpan BackoffFor(int retryNumber)
    {
        if (retryNumber < 1 || InitialBackoff <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var doublings = Math.Min(retryNumber - 1, MaxBackoffDoublings);
        var ticks = InitialBackoff.Ticks > long.MaxValue >> doublings
            ? long.MaxValue
            : InitialBackoff.Ticks << doublings;

        return MaxBackoff > TimeSpan.Zero && ticks > MaxBackoff.Ticks
            ? MaxBackoff
            : TimeSpan.FromTicks(ticks);
    }
}
