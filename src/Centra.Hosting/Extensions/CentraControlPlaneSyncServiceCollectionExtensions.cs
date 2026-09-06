using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

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

        return services;
    }
}
