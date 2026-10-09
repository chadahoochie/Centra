using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.HA;
using Centra.ControlPlane.Secrets;
using Centra.ControlPlane.Security;
using Centra.ControlPlane.Serialization;
using Centra.ControlPlane.Sync;
using Centra.ControlPlane.Topology;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.ControlPlane.Extensions;

public static class ControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddCentraControlPlane(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ControlPlaneJsonSerializerContext.Default);
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryComponentCatalog>();
        services.TryAddSingleton<IComponentCatalog>(sp => sp.GetRequiredService<InMemoryComponentCatalog>());
        services.TryAddSingleton<IComponentCatalogReader>(sp => sp.GetRequiredService<InMemoryComponentCatalog>());
        services.TryAddSingleton<IComponentCatalogWriter>(sp => sp.GetRequiredService<InMemoryComponentCatalog>());

        services.TryAddSingleton<InMemorySecretStore>();
        services.TryAddSingleton<IControlPlaneSecretResolver>(sp => sp.GetRequiredService<InMemorySecretStore>());

        services.TryAddSingleton<IResiliencePolicyCatalog, InMemoryResiliencePolicyCatalog>();
        services.TryAddSingleton<ITopologyTracker>(sp =>
        {
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new InMemoryTopologyTracker(timeProvider, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
        });
        services.TryAddSingleton<IComponentSyncDispatcher, ComponentSyncDispatcher>();

        services.TryAddSingleton(new ControlPlaneSecurityOptions { Enabled = false });
        services.TryAddSingleton<ClusterAdmissionNonceCache>();
        services.TryAddSingleton<IClusterAdmissionValidator, ClusterAdmissionValidator>();
        services.TryAddSingleton<ClusterAuthenticationEndpointFilter>();

        services.TryAddSingleton(new ControlPlaneLeadershipOptions { Enabled = false });
        services.TryAddSingleton<IControlPlaneLeaderTracker, ControlPlaneLeaderTracker>();
        services.TryAddSingleton<ControlPlaneLeadershipEndpointFilter>();

        return services;
    }

    public static IServiceCollection AddCentraControlPlaneSecurity(
        this IServiceCollection services,
        Action<ControlPlaneSecurityOptions>? configure = null)
    {
        var options = new ControlPlaneSecurityOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton<ClusterAdmissionNonceCache>();
        services.TryAddSingleton<IClusterAdmissionValidator, ClusterAdmissionValidator>();
        services.TryAddSingleton<ClusterAuthenticationEndpointFilter>();
        return services;
    }

    public static IServiceCollection AddCentraControlPlaneLeadership(
        this IServiceCollection services,
        Action<ControlPlaneLeadershipOptions>? configure = null)
    {
        var options = new ControlPlaneLeadershipOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton<IControlPlaneLeaderTracker, ControlPlaneLeaderTracker>();
        services.TryAddSingleton<ControlPlaneLeadershipEndpointFilter>();
        if (options.Enabled)
        {
            services.AddHostedService<ControlPlaneLeaderElectionHostedService>();
        }
        return services;
    }

    public static IServiceCollection AddCentraControlPlaneStateStoreCatalogs(
        this IServiceCollection services,
        string stateStoreName = "default")
    {
        services.AddSingleton<StateStoreComponentCatalog>(sp =>
        {
            var stateStore = sp.GetRequiredService<Centra.State.IStateStore>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new StateStoreComponentCatalog(stateStore, stateStoreName, timeProvider);
        });
        services.AddSingleton<IComponentCatalog>(sp => sp.GetRequiredService<StateStoreComponentCatalog>());
        services.AddSingleton<IComponentCatalogReader>(sp => sp.GetRequiredService<StateStoreComponentCatalog>());
        services.AddSingleton<IComponentCatalogWriter>(sp => sp.GetRequiredService<StateStoreComponentCatalog>());

        services.AddSingleton<IResiliencePolicyCatalog>(sp =>
        {
            var stateStore = sp.GetRequiredService<Centra.State.IStateStore>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new StateStoreResiliencePolicyCatalog(stateStore, stateStoreName, timeProvider);
        });

        services.AddSingleton<ITopologyTracker>(sp =>
        {
            var stateStore = sp.GetRequiredService<Centra.State.IStateStore>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new StateStoreTopologyTracker(stateStore, stateStoreName, timeProvider);
        });

        return services;
    }
}
