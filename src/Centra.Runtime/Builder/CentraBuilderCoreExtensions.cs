using Centra.Events;
using Centra.Locks;
using Centra.PubSub;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Extension methods for configuring core Centra options on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraBuilderCoreExtensions
{
    /// <summary>
    /// Configures the application identifier for this service instance in the Centra cluster.
    /// </summary>
    public static ICentraBuilder WithAppId(this ICentraBuilder builder, string appId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        builder.Services.Configure<CentraOptions>(opts => opts.AppId = appId);
        return builder;
    }

    /// <summary>
    /// Configures the default CloudEvents serialization mode (Binary or Structured).
    /// </summary>
    public static ICentraBuilder WithCloudEventMode(this ICentraBuilder builder, CloudEventMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<CentraOptions>(opts => opts.DefaultCloudEventMode = mode);
        return builder;
    }

    /// <summary>
    /// Configures the root <see cref="CentraOptions"/> via an action delegate.
    /// </summary>
    public static ICentraBuilder Configure(this ICentraBuilder builder, Action<CentraOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Configures the Control Plane connection and cluster coordination options.
    /// </summary>
    public static ICentraBuilder ConfigureControlPlane(this ICentraBuilder builder, Action<CentraControlPlaneOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.ControlPlane));
        return builder;
    }

    /// <summary>
    /// Configures the Centra State Store building block options.
    /// </summary>
    public static ICentraBuilder ConfigureState(this ICentraBuilder builder, Action<CentraStateOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.State));
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Configures the Centra Pub/Sub building block options.
    /// </summary>
    public static ICentraBuilder ConfigurePubSub(this ICentraBuilder builder, Action<CentraPubSubOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.PubSub));
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Configures the Centra Distributed Locks building block options.
    /// </summary>
    public static ICentraBuilder ConfigureLocks(this ICentraBuilder builder, Action<CentraLocksOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.Locks));
        builder.Services.Configure(configure);
        return builder;
    }
}
