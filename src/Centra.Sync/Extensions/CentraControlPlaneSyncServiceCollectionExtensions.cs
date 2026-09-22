using Centra;
using Centra.Sync;
using Centra.Sync.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraControlPlaneSyncServiceCollectionExtensions
{
    public static IServiceCollection AddCentraControlPlaneSync(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var endpoint = options.ControlPlaneEndpoint ?? options.ControlPlane.Endpoint;
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            }
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CentraControlPlaneSyncHostedService>());

        services.TryAddSingleton<ClusterTopologyProviderHostedService>();
        services.TryAddSingleton<IClusterTopologyProvider>(sp => sp.GetRequiredService<ClusterTopologyProviderHostedService>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ClusterTopologyProviderHostedService>());

        return services;
    }
}
