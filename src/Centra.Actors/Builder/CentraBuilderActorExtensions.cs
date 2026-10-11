using Centra.Actors;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Extension methods for configuring virtual actors on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraBuilderActorExtensions
{
    /// <summary>
    /// Configures the Centra Virtual Actors runtime options.
    /// </summary>
    public static ICentraBuilder ConfigureActors(this ICentraBuilder builder, Action<ActorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.Actors));
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Registers a virtual actor type and its primary actor interface.
    /// </summary>
    public static ICentraBuilder AddActor<TActor, TActorInterface>(this ICentraBuilder builder)
        where TActor : Actor, TActorInterface
        where TActorInterface : class, IActor
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddActor<TActor, TActorInterface>();
        return builder;
    }

    /// <summary>
    /// Registers a virtual actor type and its primary actor interface.
    /// </summary>
    public static ICentraBuilder AddActor(this ICentraBuilder builder, Type actorType, Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(interfaceType);
        builder.Services.AddActor(actorType, interfaceType);
        return builder;
    }
}
