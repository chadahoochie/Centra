using Centra.Registry;

namespace Centra.Providers.CosmosDb.Extensions;

public sealed class CosmosDbDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public CosmosDbDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry) => _action(registry);
}
