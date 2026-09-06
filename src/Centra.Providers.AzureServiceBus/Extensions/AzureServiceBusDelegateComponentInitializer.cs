using Centra.Registry;

namespace Centra.Providers.AzureServiceBus.Extensions;

public sealed class AzureServiceBusDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public AzureServiceBusDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry) => _action(registry);
}
