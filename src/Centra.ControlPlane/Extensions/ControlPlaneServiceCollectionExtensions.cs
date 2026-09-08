using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Secrets;
using Centra.ControlPlane.Sync;
using Centra.ControlPlane.Topology;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.ControlPlane.Extensions;

public static class ControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddCentraControlPlane(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryComponentCatalog>();
        services.AddSingleton<IComponentCatalog>(sp => sp.GetRequiredService<InMemoryComponentCatalog>());
        services.AddSingleton<IComponentCatalogReader>(sp => sp.GetRequiredService<InMemoryComponentCatalog>());
        services.AddSingleton<IComponentCatalogWriter>(sp => sp.GetRequiredService<InMemoryComponentCatalog>());

        services.AddSingleton<InMemorySecretStore>();
        services.AddSingleton<IControlPlaneSecretResolver>(sp => sp.GetRequiredService<InMemorySecretStore>());

        services.AddSingleton<IResiliencePolicyCatalog, InMemoryResiliencePolicyCatalog>();
        services.AddSingleton<ITopologyTracker>(sp =>
        {
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new InMemoryTopologyTracker(timeProvider, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
        });
        services.AddSingleton<IComponentSyncDispatcher, ComponentSyncDispatcher>();

        return services;
    }
}
