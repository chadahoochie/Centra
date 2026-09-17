using Centra.Hosting.HostedServices;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

public static class CentraTenantOffloadServiceCollectionExtensions
{
    public static IServiceCollection AddCentraTenantOffload(
        this IServiceCollection services,
        Action<TenantOffloadOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new TenantOffloadOptions()));
        }

        services.TryAddSingleton<ITenantMetricsTracker>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TenantOffloadOptions>>().Value;
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new RollingWindowTenantMetricsTracker(options, timeProvider);
        });

        services.TryAddTransient<IPubSubPublisher>(sp =>
        {
            var registry = sp.GetService<ComponentRegistry>();
            var centraOptions = sp.GetService<IOptions<Centra.Hosting.Options.CentraOptions>>()?.Value;
            var defaultPubSub = centraOptions?.DefaultPubSub ?? "pubsub";
            return (IPubSubPublisher?)registry?.GetPubSubDriver(defaultPubSub)
                ?? sp.GetService<Centra.Drivers.IPubSubDriver>()
                ?? (IPubSubPublisher?)sp.GetService<IPubSub>()
                ?? throw new InvalidOperationException("No PubSub driver or publisher registered.");
        });

        services.TryAddTransient<IPubSubSubscriber>(sp =>
        {
            var registry = sp.GetService<ComponentRegistry>();
            var centraOptions = sp.GetService<IOptions<Centra.Hosting.Options.CentraOptions>>()?.Value;
            var defaultPubSub = centraOptions?.DefaultPubSub ?? "pubsub";
            return (IPubSubSubscriber?)registry?.GetPubSubDriver(defaultPubSub)
                ?? sp.GetService<Centra.Drivers.IPubSubDriver>()
                ?? (IPubSubSubscriber?)sp.GetService<IPubSub>()
                ?? throw new InvalidOperationException("No PubSub driver or subscriber registered.");
        });

        services.TryAddSingleton<ITenantOffloadStrategy>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TenantOffloadOptions>>().Value;
            var registry = sp.GetService<ComponentRegistry>();
            var centraOptions = sp.GetService<IOptions<Centra.Hosting.Options.CentraOptions>>()?.Value;
            var defaultPubSub = centraOptions?.DefaultPubSub ?? "pubsub";
            var driver = registry?.GetPubSubDriver(defaultPubSub) ?? sp.GetService<Centra.Drivers.IPubSubDriver>();

            var publisher = sp.GetService<IPubSubPublisher>() ?? (IPubSubPublisher?)driver;
            var subscriber = sp.GetService<IPubSubSubscriber>() ?? (IPubSubSubscriber?)driver;

            var inspector = sp.GetService<IPubSubQueueInspector>() ?? (driver as IPubSubQueueInspector);

            return options.OffloadStrategy switch
            {
                TenantOffloadStrategyType.EphemeralBrokerTopic =>
                    new EphemeralBrokerTopicOffloadStrategy(
                        publisher ?? throw new InvalidOperationException($"No PubSub driver or publisher registered for tenant offload strategy '{options.OffloadStrategy}'."),
                        subscriber,
                        options,
                        timeProvider: sp.GetService<TimeProvider>(),
                        queueInspector: inspector),
                TenantOffloadStrategyType.BoundedShardBrokerTopic =>
                    new BoundedShardBrokerTopicOffloadStrategy(
                        publisher ?? throw new InvalidOperationException($"No PubSub driver or publisher registered for tenant offload strategy '{options.OffloadStrategy}'."),
                        options),
                _ => new InProcessFairSchedulerOffloadStrategy(options)
            };
        });

        services.TryAddSingleton<ITenantOffloadCoordinator>(sp =>
        {
            var tracker = sp.GetRequiredService<ITenantMetricsTracker>();
            var strategy = sp.GetRequiredService<ITenantOffloadStrategy>();
            var options = sp.GetRequiredService<IOptions<TenantOffloadOptions>>().Value;
            var timeProvider = sp.GetService<TimeProvider>();
            return new TenantOffloadCoordinator(tracker, strategy, options, timeProvider);
        });

        services.AddHostedService(sp =>
        {
            var coordinator = sp.GetRequiredService<ITenantOffloadCoordinator>();
            var options = sp.GetRequiredService<IOptions<TenantOffloadOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TenantOffloadReaperHostedService>>();
            var lockProvider = sp.GetService<Centra.Locks.IDistributedLockProvider>();
            return new TenantOffloadReaperHostedService(coordinator, options, logger, lockProvider);
        });

        return services;
    }
}
