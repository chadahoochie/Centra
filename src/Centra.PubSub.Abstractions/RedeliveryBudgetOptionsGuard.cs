namespace Centra.PubSub;

/// <summary>
/// Refuses a subscription that asks for a redelivery budget from a driver whose consume path does not
/// enforce one. Silently ignoring <see cref="PubSubSubscribeOptions.MaxRetryAttempts"/> would leave the
/// caller believing their retry loop is bounded when it is not.
/// </summary>
public static class RedeliveryBudgetOptionsGuard
{
    /// <summary>
    /// Throws <see cref="NotSupportedException"/> when <paramref name="options"/> asks for a redelivery
    /// budget, naming <paramref name="driverName"/> as the driver that cannot honor it. A
    /// <see cref="PubSubSubscribeOptions.MaxRetryAttempts"/> of 0 asks for no budget, which is what these
    /// drivers already do, so it is accepted.
    /// </summary>
    public static void ThrowIfConfigured(PubSubSubscribeOptions? options, string driverName)
    {
        if (options is null ||
            (options.MaxRetryAttempts is null or <= 0 && options.RetryInitialBackoff is null && options.RetryMaxBackoff is null))
        {
            return;
        }

        throw new NotSupportedException(
            $"{driverName} does not enforce a consumer-side redelivery budget, so MaxRetryAttempts, "
            + "RetryInitialBackoff and RetryMaxBackoff cannot be honored on this subscription. Remove them, or "
            + "subscribe through a driver that implements the budget (today only the RabbitMQ driver does).");
    }
}
