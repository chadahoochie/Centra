using Centra.Hosting.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.Hosting.Extensions;

public static class CentraServiceCollectionExtensions
{
    public static IServiceCollection AddCentra(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);
        services.AddCentraResilience();
        services.AddCentraState();
        services.AddCentraPubSub();
        services.AddCentraLocks();
        services.AddCentraBindings();
        services.AddCentraInvocation();
        services.AddCentraControlPlaneSync();

        return services;
    }
}
