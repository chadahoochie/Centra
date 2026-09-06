using Centra.Bindings;
using Centra.Hosting.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Hosting.Extensions;

public static class CentraBindingsServiceCollectionExtensions
{
    public static IServiceCollection AddCentraBindings(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddSingleton<IOutputBinding, CentraOutputBinding>();

        return services;
    }
}
