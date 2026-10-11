using Centra;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centra.Core.Workflows;

/// <summary>
/// Post-configures <see cref="WorkflowOptions"/> by cascading root store defaults from <see cref="CentraOptions"/>
/// when stores are not explicitly configured.
/// </summary>
public sealed class WorkflowOptionsPostSetup : IPostConfigureOptions<WorkflowOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public WorkflowOptionsPostSetup(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void PostConfigure(string? name, WorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = _serviceProvider.GetService<CentraOptions>() ?? _serviceProvider.GetService<IOptions<CentraOptions>>()?.Value;
        if (root is not null)
        {
            var config = _serviceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var explicitState = config?["Centra:Workflows:DefaultStateStore"];
            var explicitPubSub = config?["Centra:Workflows:DefaultPubSub"];
            var explicitLock = config?["Centra:Workflows:DefaultLockStore"];

            if (explicitState is not null)
            {
                options.DefaultStateStore = explicitState;
            }
            else if (!options.HasExplicitStateStore)
            {
                options.DefaultStateStore = root.DefaultStateStore;
            }

            if (explicitPubSub is not null)
            {
                options.DefaultPubSub = explicitPubSub;
            }
            else if (!options.HasExplicitPubSub)
            {
                options.DefaultPubSub = root.DefaultPubSub;
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
