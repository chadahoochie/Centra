using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Extension methods for configuring service invocation RPC clients on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraBuilderInvocationExtensions
{
    /// <summary>
    /// Registers a strongly-typed service invocation client interface proxy.
    /// </summary>
    public static ICentraBuilder AddServiceClient<TInterface>(this ICentraBuilder builder)
        where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraServiceClient<TInterface>();
        return builder;
    }
}
