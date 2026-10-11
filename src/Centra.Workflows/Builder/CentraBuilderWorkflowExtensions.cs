using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Extension methods for configuring distributed workflows on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraBuilderWorkflowExtensions
{
    /// <summary>
    /// Configures the Centra Distributed Workflows runtime options.
    /// </summary>
    public static ICentraBuilder ConfigureWorkflows(this ICentraBuilder builder, Action<WorkflowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.Configure<CentraOptions>(opts => configure(opts.Workflows));
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Registers a distributed workflow type in DI and the workflow registry.
    /// </summary>
    public static ICentraBuilder AddWorkflow<TWorkflow>(this ICentraBuilder builder)
        where TWorkflow : class, IWorkflow
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddWorkflow<TWorkflow>();
        return builder;
    }

    /// <summary>
    /// Registers a workflow activity type in DI and the activity registry.
    /// </summary>
    public static ICentraBuilder AddWorkflowActivity<TActivity>(this ICentraBuilder builder)
        where TActivity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddWorkflowActivity<TActivity>();
        return builder;
    }
}
