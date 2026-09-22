using System.Reflection;
using Centra;
using Centra.Hosting.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Centra concerns and scans the given assemblies (or the entry assembly, if
    /// none are given) for attribute-decorated components ([Topic], [Workflow],
    /// [WorkflowActivity], [CronBinding], [Binding], [ServiceClient], [Actor]) so the attribute
    /// alone is enough to register them - explicit .AddCentraX&lt;T&gt;() calls remain available
    /// as optional overrides rather than mandatory duplicate wiring.
    /// </summary>
    public static IServiceCollection AddCentra(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null,
        params Assembly[] assembliesToScan)
    {
        services.AddCentraCore(configure);
        services.AddCentraSerialization();
        services.AddCentraResilience();
        services.AddCentraState();
        services.AddCentraPubSub();
        services.AddCentraLocks();
        services.AddCentraBindings();
        services.AddCentraInvocation();
        services.AddCentraActors();
        services.AddCentraWorkflows();
        services.AddCentraControlPlaneSync();

        var assemblies = assembliesToScan.Length > 0
            ? assembliesToScan
            : new[] { Assembly.GetEntryAssembly()! };
        CentraAttributeScanner.ScanAndRegister(services, assemblies);

        return services;
    }
}
