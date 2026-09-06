using Centra.Components;
using Centra.Providers.PostgreSql.Locks;
using Centra.Providers.PostgreSql.Options;
using Centra.Providers.PostgreSql.State;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.PostgreSql.Extensions;

public sealed class PostgreSqlComponentInitializer : IComponentInitializer
{
    private readonly PostgreSqlStateStoreDriver _stateDriver;
    private readonly PostgreSqlDistributedLockDriver _lockDriver;
    private readonly IOptions<PostgreSqlProviderOptions> _options;

    public PostgreSqlComponentInitializer(
        PostgreSqlStateStoreDriver stateDriver,
        PostgreSqlDistributedLockDriver lockDriver,
        IOptions<PostgreSqlProviderOptions> options)
    {
        _stateDriver = stateDriver ?? throw new ArgumentNullException(nameof(stateDriver));
        _lockDriver = lockDriver ?? throw new ArgumentNullException(nameof(lockDriver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Initialize(ComponentRegistry registry)
    {
        var opts = _options.Value;

        registry.RegisterStateStoreDriver(opts.DefaultStateStoreName, _stateDriver);
        registry.RegisterLockDriver(opts.DefaultLockStoreName, _lockDriver);

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultStateStoreName,
            Type = ComponentType.StateStore,
            Provider = "postgresql"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultLockStoreName,
            Type = ComponentType.DistributedLock,
            Provider = "postgresql"
        });
    }
}
