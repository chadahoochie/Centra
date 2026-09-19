namespace Centra.PubSub;

/// <summary>
/// Optional capability for pub/sub drivers that await in-flight handlers before closing a
/// subscription. A host tears subscriptions down one topic at a time, so without a shared window
/// each teardown would claim the driver's whole drain allowance and the total cost would scale with
/// the number of topics. Opening a window before the teardown loop makes every subscription drained
/// inside it share one deadline.
/// </summary>
public interface IPubSubShutdownDrain
{
    /// <summary>
    /// Opens the shutdown drain window: every subscription torn down until the returned handle is
    /// disposed shares a single drain deadline, starting now. Disposing the handle closes the window,
    /// so a later teardown outside a shutdown gets the full allowance again.
    /// </summary>
    IDisposable BeginShutdownDrain();
}
