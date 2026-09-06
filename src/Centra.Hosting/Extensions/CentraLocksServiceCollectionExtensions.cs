using Centra.Hosting.Options;
using Centra.Locks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Hosting.Extensions;

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
