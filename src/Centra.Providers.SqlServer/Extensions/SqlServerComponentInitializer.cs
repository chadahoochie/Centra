using Centra.Components;
using Centra.Providers.SqlServer.Locks;
using Centra.Providers.SqlServer.Options;
using Centra.Providers.SqlServer.State;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.SqlServer.Extensions;

public sealed class SqlServerComponentInitializer : IComponentInitializer
{
    private readonly SqlServerStateStoreDriver _stateDriver;
    private readonly SqlServerDistributedLockDriver _lockDriver;
    private readonly IOptions<SqlServerProviderOptions> _options;

    public SqlServerComponentInitializer(
        SqlServerStateStoreDriver stateDriver,
        SqlServerDistributedLockDriver lockDriver,
        IOptions<SqlServerProviderOptions> options)
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
            Provider = "sqlserver"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultLockStoreName,
            Type = ComponentType.DistributedLock,
            Provider = "sqlserver"
        });
    }
}
