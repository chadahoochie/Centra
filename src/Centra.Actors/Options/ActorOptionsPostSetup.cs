using Centra;
using Centra.Actors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centra.Core.Actors;

/// <summary>
/// Post-configures <see cref="ActorOptions"/> by cascading root store defaults from <see cref="CentraOptions"/>
/// when stores are not explicitly configured.
/// </summary>
public sealed class ActorOptionsPostSetup : IPostConfigureOptions<ActorOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public ActorOptionsPostSetup(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void PostConfigure(string? name, ActorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = _serviceProvider.GetService<CentraOptions>() ?? _serviceProvider.GetService<IOptions<CentraOptions>>()?.Value;
        if (root is not null)
        {
            var config = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var explicitState = config?["Centra:Actors:DefaultStateStore"];
            var explicitLock = config?["Centra:Actors:DefaultLockStore"];

            if (explicitState is not null)
            {
                options.DefaultStateStore = explicitState;
            }
            else if (!options.HasExplicitStateStore)
            {
                options.DefaultStateStore = root.DefaultStateStore;
            }

            if (explicitLock is not null)
            {
                options.DefaultLockStore = explicitLock;
            }
            else if (!options.HasExplicitLockStore)
            {
                options.DefaultLockStore = root.DefaultLockStore;
            }
        }
    }
}
