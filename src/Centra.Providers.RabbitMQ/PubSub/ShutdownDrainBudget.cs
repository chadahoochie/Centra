namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Maps a configured shutdown drain allowance onto the only two shapes a drain can actually wait in,
/// so no configured value can reach <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> out of
/// range. <see cref="Timeout.InfiniteTimeSpan"/> and every other negative duration mean unbounded, as
/// does anything longer than the timer subsystem can wait - including
/// <see cref="TimeSpan.MaxValue"/>. <see cref="TimeSpan.Zero"/> means do not wait. Everything else is
/// returned unchanged. Total by construction: a misconfigured duration never degrades silently to no
/// drain at all, and never throws at shutdown.
/// </summary>
internal static class ShutdownDrainBudget
{
    // Task.WaitAsync rejects anything above the timer subsystem's own ceiling (uint.MaxValue - 1
    // milliseconds, roughly 49.7 days) with ArgumentOutOfRangeException.
    private static readonly TimeSpan LongestWaitable = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    public static TimeSpan Normalize(TimeSpan configured)
        => configured < TimeSpan.Zero || configured > LongestWaitable
            ? Timeout.InfiniteTimeSpan
            : configured;
}
