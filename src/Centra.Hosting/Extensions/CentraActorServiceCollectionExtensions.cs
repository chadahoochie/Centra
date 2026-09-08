using Centra.Actors;
using Centra.Core.Actors;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Invocation;
using Centra.Locks;
using Centra.State;
using Centra.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

public static class CentraActorServiceCollectionExtensions
{
    public static IServiceCollection AddCentraActors(
        this IServiceCollection services,
        Action<ActorOptions>? configure = null)
    {
        services.AddOptions<ActorOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<ActorOptions>>().Value);

        services.TryAddSingleton<IActorPlacementDirector>(sp =>
        {
            var centraOptions = sp.GetService<CentraOptions>();
            var localAppId = centraOptions?.AppId ?? Environment.MachineName;
            var localNodeId = centraOptions?.ControlPlane.InstanceId ?? Environment.MachineName;

            var ring = new ConsistentHashRing();
            ring.AddNode(localNodeId);

            var topologyProvider = sp.GetService<IClusterTopologyProvider>();
            if (topologyProvider is not null)
            {
                // Actor placement only cares about replicas of this same logical service (AppId) -
                // invocation routing separately load-balances across AppId, but the ring must pick
                // exactly one physical InstanceId to own a given actor among *our* replicas.
                foreach (var node in topologyProvider.GetSnapshot())
                {
                    if (string.Equals(node.AppId, localAppId, StringComparison.OrdinalIgnoreCase))
                    {
                        ring.AddNode(node.InstanceId);
                    }
                }

                topologyProvider.TopologyChanged += (_, e) =>
                {
                    foreach (var node in e.AddedNodes)
                    {
                        if (string.Equals(node.AppId, localAppId, StringComparison.OrdinalIgnoreCase))
                        {
                            ring.AddNode(node.InstanceId);
                        }
                    }

                    foreach (var node in e.RemovedNodes)
                    {
                        if (string.Equals(node.AppId, localAppId, StringComparison.OrdinalIgnoreCase))
                        {
                            ring.RemoveNode(node.InstanceId);
                        }
                    }
                };
            }

            return new ActorPlacementDirector(localNodeId, ring);
        });

        services.TryAddSingleton<ActorManager>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var opt = sp.GetRequiredService<ActorOptions>();
            var centraOpt = sp.GetService<CentraOptions>();
            var storeName = !string.IsNullOrWhiteSpace(opt.DefaultStateStore) && opt.DefaultStateStore != "statestore"
                ? opt.DefaultStateStore
                : (centraOpt?.DefaultStateStore ?? opt.DefaultStateStore);
            opt.DefaultStateStore = storeName;

            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var registrations = sp.GetServices<ActorRegistration>();
            return new ActorManager(sp, stateStore, opt, timeProvider, registrations);
        });

        services.TryAddSingleton<ActorReminderCoordinator>(sp =>
        {
            var manager = sp.GetRequiredService<ActorManager>();
            var stateStore = sp.GetRequiredService<IStateStore>();
            var opt = sp.GetRequiredService<ActorOptions>();
            var centraOpt = sp.GetService<CentraOptions>();
            var storeName = !string.IsNullOrWhiteSpace(opt.DefaultStateStore) && opt.DefaultStateStore != "statestore"
                ? opt.DefaultStateStore
                : (centraOpt?.DefaultStateStore ?? opt.DefaultStateStore);
            opt.DefaultStateStore = storeName;

            var lockProvider = sp.GetService<IDistributedLockProvider>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new ActorReminderCoordinator(manager, stateStore, opt, lockProvider, timeProvider);
        });

        services.TryAddSingleton<IActorProxyFactory>(sp =>
        {
            var manager = sp.GetRequiredService<ActorManager>();
            var placement = sp.GetRequiredService<IActorPlacementDirector>();
            var invoker = sp.GetService<IServiceInvoker>();
            return new ActorProxyFactory(manager, placement, invoker);
        });

        services.AddHostedService<CentraActorHostedService>();

        return services;
    }

    public static IServiceCollection AddCentraActor<TActor, TActorInterface>(
        this IServiceCollection services)
        where TActor : Actor, TActorInterface
        where TActorInterface : class, IActor
    {
        services.TryAddTransient<TActor>();

        services.ReplaceRegistrationFor<ActorRegistration>(
            r => r.ActorType == typeof(TActor),
            new ActorRegistration(typeof(TActor), typeof(TActorInterface)));

        return services;
    }

    public static IServiceCollection AddActor<TActor, TActorInterface>(
        this IServiceCollection services)
        where TActor : Actor, TActorInterface
        where TActorInterface : class, IActor =>
        services.AddCentraActor<TActor, TActorInterface>();
}
