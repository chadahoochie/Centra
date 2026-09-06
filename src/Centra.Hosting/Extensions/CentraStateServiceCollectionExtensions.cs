using Centra.Hosting.Options;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Hosting.Extensions;

public static class CentraStateServiceCollectionExtensions
{
    public static IServiceCollection AddCentraState(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddSingleton<IStateStore, CentraStateStore>();
        services.TryAddTransient(typeof(IStateStore<>), typeof(CentraStateStoreRegistrationHelper<>));

        return services;
    }

    public static IServiceCollection AddCentraStateStore<T>(this IServiceCollection services, string storeName)
    {
        services.TryAddTransient<IStateStore<T>>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            return new CentraStateStore<T>(stateStore, storeName);
        });

        return services;
    }
}
