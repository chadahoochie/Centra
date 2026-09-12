namespace Centra.PubSub.Outbox;

/// <summary>
/// Configuration options for the transactional outbox background dispatcher.
/// </summary>
public sealed record OutboxOptions
{
    /// <summary>
    /// Gets or sets the default Pub/Sub broker name to use if not specified at publish time.
    /// </summary>
    public string? DefaultPubSubName { get; init; }

    /// <summary>
    /// Gets or sets the polling interval for draining pending outbox messages.
    /// Defaults to 250 milliseconds.
    /// </summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets the batch size of messages fetched from the outbox on each sweep.
    /// Defaults to 50.
    /// </summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Gets or sets the maximum delivery attempts before marking a message permanently failed.
    /// Defaults to 5.
    /// </summary>
    public int MaxDeliveryAttempts { get; init; } = 5;
}
