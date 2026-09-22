using Centra.Registry;

namespace Centra.Providers.PostgreSql.Extensions;

public sealed class PostgreSqlDelegateComponentInitializer : IComponentInitializer
{
    private readonly Action<ComponentRegistry> _action;

    public PostgreSqlDelegateComponentInitializer(Action<ComponentRegistry> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Initialize(ComponentRegistry registry) => _action(registry);
}
