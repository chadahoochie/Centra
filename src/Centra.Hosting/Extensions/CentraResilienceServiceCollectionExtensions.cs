using Centra.Hosting.Options;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

/// <summary>
/// Extension methods for configuring Centra resilience pipelines and policies in DI.
/// </summary>
public static class CentraResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Adds Centra resilience pipeline providers, policy registries, and Polly v8 execution engines to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraResilience(
        this IServiceCollection services,
        Action<CentraResilienceOptions>? configure = null)
    {
        services.AddOptions<CentraResilienceOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<PollyResiliencePipelineRegistry>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var options = sp.GetService<IOptions<CentraResilienceOptions>>()?.Value ?? new CentraResilienceOptions();
            var registry = new PollyResiliencePipelineRegistry(loggerFactory, timeProvider);

            foreach (var policy in options.Policies)
            {
                registry.RegisterPolicy(policy);
            }

            return registry;
        });

        services.TryAddSingleton<IResiliencePipelineProvider>(sp => sp.GetRequiredService<PollyResiliencePipelineRegistry>());
        services.TryAddSingleton<IResiliencePolicyRegistry>(sp => sp.GetRequiredService<PollyResiliencePipelineRegistry>());

        return services;
    }
}
