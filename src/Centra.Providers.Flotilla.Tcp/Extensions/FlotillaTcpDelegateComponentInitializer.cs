using Centra.Drivers;
using Centra.Registry;

namespace Centra.Providers.Flotilla.Tcp.Extensions;

/// <summary>
/// Delegate-based component initializer for named Flotilla TCP pub/sub registrations.
/// </summary>
public sealed class FlotillaTcpDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public FlotillaTcpDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry)
    {
        _action(registry);
    }
}
