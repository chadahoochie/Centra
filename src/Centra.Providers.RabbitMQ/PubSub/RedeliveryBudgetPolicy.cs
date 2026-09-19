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
    /// Multiple of the retry schedule an attempt counter is kept for beyond the point the message could still
    /// legitimately come back, so a live counter is never reclaimed early.
    /// </summary>
    public const int StalenessSafetyFactor = 4;

    /// <summary>
    /// Floor applied to <see cref="StaleAfter"/>, so a subscription with little or no backoff still keeps its
    /// counters long enough to survive ordinary redelivery latency.
    /// </summary>
    public static readonly TimeSpan MinStaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ceiling applied to <see cref="StaleAfter"/>, bounding how long an abandoned counter can occupy memory
    /// however extreme the configured backoff is.
    /// </summary>
    public static readonly TimeSpan MaxStaleAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// How long an attempt counter survives without being charged again before it is treated as stale. Sized
    /// off the retry schedule - the whole remaining backoff sequence times
    /// <see cref="StalenessSafetyFactor"/> - so a message that could still legitimately be redelivered always
    /// finds its counter, while a message that never comes back stops occupying memory.
    /// </summary>
    public TimeSpan StaleAfter
    {
        get
        {
            var retries = Math.Max(MaxRetryAttempts, 1);
            var perRetryTicks = BackoffFor(MaxRetryAttempts).Ticks;
            var scheduleTicks = perRetryTicks >= MaxStaleAfter.Ticks / retries
                ? MaxStaleAfter.Ticks
                : perRetryTicks * retries;
            var slackedTicks = scheduleTicks >= MaxStaleAfter.Ticks / StalenessSafetyFactor
                ? MaxStaleAfter.Ticks
                : scheduleTicks * StalenessSafetyFactor;

            return TimeSpan.FromTicks(Math.Max(slackedTicks, MinStaleAfter.Ticks));
        }
    }

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
