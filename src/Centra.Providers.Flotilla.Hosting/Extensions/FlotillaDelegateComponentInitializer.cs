using Centra.Registry;

namespace Centra.Providers.Flotilla.Hosting.Extensions;

/// <summary>
/// Action-based component initializer for registering custom named Flotilla pub/sub instances.
/// </summary>
public sealed class FlotillaDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public FlotillaDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry) => _action(registry);
}
