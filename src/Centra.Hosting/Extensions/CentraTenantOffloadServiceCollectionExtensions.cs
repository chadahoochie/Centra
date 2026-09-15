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

        services.TryAddSingleton<ITenantOffloadStrategy>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TenantOffloadOptions>>().Value;
            return options.OffloadStrategy switch
            {
                TenantOffloadStrategyType.EphemeralBrokerTopic =>
                    new EphemeralBrokerTopicOffloadStrategy(
                        sp.GetRequiredService<IPubSubPublisher>(),
                        sp.GetService<IPubSubSubscriber>(),
                        options),
                TenantOffloadStrategyType.BoundedShardBrokerTopic =>
                    new BoundedShardBrokerTopicOffloadStrategy(
                        sp.GetRequiredService<IPubSubPublisher>(),
                        options),
                _ => new InProcessFairSchedulerOffloadStrategy(options)
            };
        });

        services.TryAddSingleton<ITenantOffloadCoordinator>(sp =>
        {
            var tracker = sp.GetRequiredService<ITenantMetricsTracker>();
            var strategy = sp.GetRequiredService<ITenantOffloadStrategy>();
            var options = sp.GetRequiredService<IOptions<TenantOffloadOptions>>().Value;
            return new TenantOffloadCoordinator(tracker, strategy, options);
        });

        services.AddHostedService<TenantOffloadReaperHostedService>();

        return services;
    }
}
