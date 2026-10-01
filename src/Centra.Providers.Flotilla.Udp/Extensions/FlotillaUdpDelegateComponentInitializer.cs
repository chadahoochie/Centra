using Centra.Drivers;
using Centra.Registry;

namespace Centra.Providers.Flotilla.Udp.Extensions;

/// <summary>
/// Delegate-based component initializer for named Flotilla UDP pub/sub registrations.
/// </summary>
public sealed class FlotillaUdpDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public FlotillaUdpDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry)
    {
        _action(registry);
    }
}
