namespace Centra.PubSub.Inbox;

/// <summary>
/// Configuration options for the idempotent inbox message deduplication engine.
/// </summary>
public sealed record InboxOptions
{
    /// <summary>
    /// Gets or sets the retention period for processed message identifiers before deduplication entries expire.
    /// Defaults to 7 days.
    /// </summary>
    public TimeSpan DuplicateRetentionPeriod { get; init; } = TimeSpan.FromDays(7);
}
