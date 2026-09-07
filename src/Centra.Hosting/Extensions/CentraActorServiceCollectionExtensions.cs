using Centra.Actors;
using Centra.Core.Actors;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Invocation;
using Centra.Locks;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Hosting.Extensions;

public static class CentraActorServiceCollectionExtensions
{
    public static IServiceCollection AddCentraActors(
        this IServiceCollection services,
        Action<ActorOptions>? configure = null)
    {
        var options = new ActorOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<ConsistentHashRing>(sp => new ConsistentHashRing());
        services.TryAddSingleton<IActorPlacementDirector>(sp =>
        {
            var ring = sp.GetRequiredService<ConsistentHashRing>();
            var centraOptions = sp.GetService<CentraOptions>();
            var localNodeId = centraOptions?.AppId ?? Environment.MachineName;
            ring.AddNode(localNodeId);
            return new ActorPlacementDirector(localNodeId, ring);
        });

        services.TryAddSingleton<ActorManager>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var opt = sp.GetRequiredService<ActorOptions>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var registrations = sp.GetServices<ActorRegistration>();
            return new ActorManager(sp, stateStore, opt, timeProvider, registrations);
        });

        services.TryAddSingleton<ActorReminderCoordinator>(sp =>
        {
            var manager = sp.GetRequiredService<ActorManager>();
            var stateStore = sp.GetRequiredService<IStateStore>();
            var opt = sp.GetRequiredService<ActorOptions>();
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
        services.AddSingleton(new ActorRegistration(typeof(TActor), typeof(TActorInterface)));

        return services;
    }

    public static IServiceCollection AddActor<TActor, TActorInterface>(
        this IServiceCollection services)
        where TActor : Actor, TActorInterface
        where TActorInterface : class, IActor =>
        services.AddCentraActor<TActor, TActorInterface>();
}
