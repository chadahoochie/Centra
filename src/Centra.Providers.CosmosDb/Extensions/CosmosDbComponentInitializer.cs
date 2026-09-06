using Centra.Components;
using Centra.Providers.CosmosDb.Locks;
using Centra.Providers.CosmosDb.Options;
using Centra.Providers.CosmosDb.State;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.CosmosDb.Extensions;

public sealed class CosmosDbComponentInitializer : IComponentInitializer
{
    private readonly CosmosDbStateStoreDriver _stateDriver;
    private readonly CosmosDbDistributedLockDriver _lockDriver;
    private readonly IOptions<CosmosDbProviderOptions> _options;

    public CosmosDbComponentInitializer(
        CosmosDbStateStoreDriver stateDriver,
        CosmosDbDistributedLockDriver lockDriver,
        IOptions<CosmosDbProviderOptions> options)
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
            Provider = "cosmosdb"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultLockStoreName,
            Type = ComponentType.DistributedLock,
            Provider = "cosmosdb"
        });
    }
}
