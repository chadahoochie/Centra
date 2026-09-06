using Centra.Registry;

namespace Centra.Providers.SqlServer.Extensions;

public sealed class SqlServerDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public SqlServerDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry) => _action(registry);
}
