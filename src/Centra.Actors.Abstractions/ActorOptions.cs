namespace Centra.Actors;

/// <summary>
/// Configuration options for the Centra Virtual Actors runtime.
/// </summary>
public sealed class ActorOptions
{
    public TimeSpan ActorIdleTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan ActorScanInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReminderInterval { get; set; } = TimeSpan.FromSeconds(10);
    public string DefaultStateStore { get; set; } = "statestore";
    public string DefaultLockStore { get; set; } = "lockstore";
}
