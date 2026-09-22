using Centra;
using Centra.Locks;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraLocksServiceCollectionExtensions
{
    public static IServiceCollection AddCentraLocks(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddSingleton<IDistributedLockProvider, CentraDistributedLockProvider>();

        return services;
    }
}
