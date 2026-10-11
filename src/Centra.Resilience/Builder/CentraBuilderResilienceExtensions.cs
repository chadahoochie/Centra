using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Extension methods for configuring resilience policies on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraBuilderResilienceExtensions
{
    /// <summary>
    /// Configures the Centra Resilience options.
    /// </summary>
    public static ICentraBuilder ConfigureResilience(this ICentraBuilder builder, Action<CentraResilienceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.Resilience));
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Adds a resilience policy definition to the builder configuration.
    /// </summary>
    public static ICentraBuilder AddResiliencePolicy(this ICentraBuilder builder, CentraResiliencePolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);
        builder.Services.Configure<CentraResilienceOptions>(opts => opts.AddPolicy(definition));
        builder.Services.Configure<CentraOptions>(opts => opts.Resilience.AddPolicy(definition));
        return builder;
    }
}
