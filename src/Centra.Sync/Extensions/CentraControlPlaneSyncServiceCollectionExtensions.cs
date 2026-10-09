using Centra;
using Centra.Sync;
using Centra.Sync.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraControlPlaneSyncServiceCollectionExtensions
{
    public static IServiceCollection AddCentraControlPlaneSync(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddSingleton<ControlPlaneEndpointSelector>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var endpoints = options.ControlPlane.Endpoints.Count > 0
                ? options.ControlPlane.Endpoints
                : (!string.IsNullOrWhiteSpace(options.ControlPlaneEndpoint ?? options.ControlPlane.Endpoint)
                    ? new[] { (options.ControlPlaneEndpoint ?? options.ControlPlane.Endpoint)! }
                    : Array.Empty<string>());
            return new ControlPlaneEndpointSelector(endpoints.Count > 0 ? endpoints : new[] { "http://localhost:8080" });
        });

        services.TryAddTransient<ControlPlaneSecurityHeadersHandler>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var selector = sp.GetRequiredService<ControlPlaneEndpointSelector>();
            return new ControlPlaneSecurityHeadersHandler(options.ControlPlane, selector);
        });

        services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>((sp, http) =>
        {
            var selector = sp.GetRequiredService<ControlPlaneEndpointSelector>();
            var endpoint = selector.GetCurrentEndpoint();
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            }
        }).AddHttpMessageHandler<ControlPlaneSecurityHeadersHandler>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CentraControlPlaneSyncHostedService>());

        services.TryAddSingleton<ClusterTopologyProviderHostedService>(sp =>
        {
            var client = sp.GetRequiredService<IControlPlaneClient>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var logger = sp.GetService<ILogger<ClusterTopologyProviderHostedService>>();
            return new ClusterTopologyProviderHostedService(client, options.ControlPlane.ClusterId, logger);
        });
        services.TryAddSingleton<IClusterTopologyProvider>(sp => sp.GetRequiredService<ClusterTopologyProviderHostedService>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ClusterTopologyProviderHostedService>());

        return services;
    }
}
