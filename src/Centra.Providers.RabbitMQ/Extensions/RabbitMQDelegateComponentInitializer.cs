using Centra.Registry;

namespace Centra.Providers.RabbitMQ.Extensions;

public sealed class RabbitMQDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public RabbitMQDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry) => _action(registry);
}
