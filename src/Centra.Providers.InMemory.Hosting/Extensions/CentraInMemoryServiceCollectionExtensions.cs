using Centra.Drivers;
using Centra.Providers.InMemory.Bindings;
using Centra.Providers.InMemory.Locks;
using Centra.Providers.InMemory.PubSub;
using Centra.Providers.InMemory.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Providers.InMemory.Extensions;

public static class CentraInMemoryServiceCollectionExtensions
{
    public static IServiceCollection AddCentraInMemory(
        this IServiceCollection services,
        string defaultStateStore = "statestore",
        string defaultPubSub = "pubsub",
        string defaultLockStore = "lockstore")
    {
        services.TryAddSingleton<InMemoryStateStoreDriver>();
        services.TryAddSingleton<InMemoryPubSubDriver>();
        services.TryAddSingleton<InMemoryDistributedLockDriver>();
        services.TryAddSingleton<InMemoryBindingDriver>();

        services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<InMemoryStateStoreDriver>());
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<InMemoryPubSubDriver>());
        services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<InMemoryDistributedLockDriver>());
        services.AddSingleton<IBindingDriver>(sp => sp.GetRequiredService<InMemoryBindingDriver>());

        services.AddSingleton<IComponentInitializer>(sp => new InMemoryComponentInitializer(
            sp.GetRequiredService<InMemoryStateStoreDriver>(),
            sp.GetRequiredService<InMemoryPubSubDriver>(),
            sp.GetRequiredService<InMemoryDistributedLockDriver>(),
            sp.GetRequiredService<InMemoryBindingDriver>(),
            defaultStateStore,
            defaultPubSub,
            defaultLockStore));

        return services;
    }
}
