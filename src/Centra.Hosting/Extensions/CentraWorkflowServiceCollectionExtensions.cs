using System.Reflection;
using Centra.Core.Workflows;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.Locks;
using Centra.Resilience;
using Centra.Serialization;
using Centra.State;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

/// <summary>
/// Dependency injection extension methods for registering Centra Workflows and Sagas.
/// </summary>
public static class CentraWorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddCentraWorkflows(
        this IServiceCollection services,
        Action<WorkflowOptions>? configure = null)
    {
        services.AddOptions<WorkflowOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<WorkflowOptions>>().Value);

        services.TryAddSingleton<IWorkflowRegistry>(sp =>
        {
            var registry = new WorkflowRegistry();
            foreach (var reg in sp.GetServices<CentraWorkflowRegistration>())
            {
                registry.RegisterWorkflow(reg.Definition);
            }
            foreach (var reg in sp.GetServices<CentraWorkflowActivityRegistration>())
            {
                registry.RegisterActivity(reg.Definition);
            }
            return registry;
        });

        services.TryAddSingleton<IWorkflowHistoryStore>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var opt = sp.GetRequiredService<WorkflowOptions>();
            var centraOpt = sp.GetService<CentraOptions>();
            var storeName = !string.IsNullOrWhiteSpace(opt.DefaultStateStore) && opt.DefaultStateStore != "statestore"
                ? opt.DefaultStateStore
                : (centraOpt?.DefaultStateStore ?? opt.DefaultStateStore);
            return new WorkflowHistoryStore(stateStore, storeName);
        });

        services.TryAddSingleton<IWorkflowActivityDispatcher>(sp =>
        {
            var registry = sp.GetRequiredService<IWorkflowRegistry>();
            var serializer = sp.GetRequiredService<ICentraSerializer>();
            var resilience = sp.GetService<IResiliencePipelineProvider>();
            return new WorkflowActivityDispatcher(sp, registry, serializer, resilience);
        });

        services.TryAddSingleton<IDurableWorkflowTimerStore>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var opt = sp.GetRequiredService<WorkflowOptions>();
            var centraOpt = sp.GetService<CentraOptions>();
            var storeName = !string.IsNullOrWhiteSpace(opt.DefaultStateStore) && opt.DefaultStateStore != "statestore"
                ? opt.DefaultStateStore
                : (centraOpt?.DefaultStateStore ?? opt.DefaultStateStore);
            return new StateStoreDurableWorkflowTimerStore(stateStore, storeName);
        });

        services.TryAddSingleton<IWorkflowEngine>(sp =>
        {
            var registry = sp.GetRequiredService<IWorkflowRegistry>();
            var historyStore = sp.GetRequiredService<IWorkflowHistoryStore>();
            var dispatcher = sp.GetRequiredService<IWorkflowActivityDispatcher>();
            var serializer = sp.GetRequiredService<ICentraSerializer>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var timerStore = sp.GetService<IDurableWorkflowTimerStore>();
            var logger = sp.GetService<ILogger<WorkflowEngine>>();
            return new WorkflowEngine(sp, registry, historyStore, dispatcher, serializer, timeProvider, logger, durableTimerStore: timerStore);
        });

        services.TryAddSingleton<DurableWorkflowTimerCoordinator>(sp =>
        {
            var timerStore = sp.GetRequiredService<IDurableWorkflowTimerStore>();
            var engine = sp.GetRequiredService<IWorkflowEngine>();
            var lockProvider = sp.GetService<IDistributedLockProvider>();
            var centraOpt = sp.GetService<CentraOptions>();
            var lockStore = centraOpt?.DefaultLockStore ?? "lockstore";
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var logger = sp.GetService<ILogger<DurableWorkflowTimerCoordinator>>();
            return new DurableWorkflowTimerCoordinator(
                timerStore,
                id => engine.FireTimerAsync(id),
                lockProvider,
                lockStore,
                timeProvider,
                logger);
        });

        services.TryAddSingleton<IWorkflowClient, WorkflowClient>();

        services.AddHostedService<CentraWorkflowHostedService>();

        return services;
    }

    public static IServiceCollection AddCentraWorkflow<TWorkflow>(this IServiceCollection services)
        where TWorkflow : class, IWorkflow
    {
        services.TryAddTransient<TWorkflow>();

        var workflowType = typeof(TWorkflow);
        var attr = workflowType.GetCustomAttribute<WorkflowAttribute>();
        var name = attr?.Name ?? workflowType.Name;

        Type inputType = typeof(void);
        Type outputType = typeof(void);

        var current = workflowType;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var genericDef = current.GetGenericTypeDefinition();
                if (genericDef == typeof(Workflow<,>))
                {
                    var args = current.GetGenericArguments();
                    inputType = args[0];
                    outputType = args[1];
                    break;
                }
                if (genericDef == typeof(Workflow<>))
                {
                    inputType = current.GetGenericArguments()[0];
                    outputType = typeof(void);
                    break;
                }
            }
            current = current.BaseType;
        }

        var definition = new WorkflowDefinition(name, workflowType, inputType, outputType);
        services.ReplaceRegistrationFor<CentraWorkflowRegistration>(
            r => r.Definition.WorkflowType == workflowType,
            new CentraWorkflowRegistration(definition));

        return services;
    }

    public static IServiceCollection AddCentraWorkflowActivity<TActivity>(this IServiceCollection services)
        where TActivity : class
    {
        services.TryAddTransient<TActivity>();

        var actType = typeof(TActivity);
        var attr = actType.GetCustomAttribute<WorkflowActivityAttribute>();
        var name = attr?.Name ?? actType.Name;

        Type inputType = typeof(void);
        Type outputType = typeof(void);

        foreach (var iface in actType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IWorkflowActivity<,>))
            {
                var args = iface.GetGenericArguments();
                inputType = args[0];
                outputType = args[1];
                break;
            }
        }

        var definition = new WorkflowActivityDefinition(name, actType, inputType, outputType);
        services.ReplaceRegistrationFor<CentraWorkflowActivityRegistration>(
            r => r.Definition.ActivityType == actType,
            new CentraWorkflowActivityRegistration(definition));

        return services;
    }

    public static IServiceCollection AddWorkflow<TWorkflow>(this IServiceCollection services)
        where TWorkflow : class, IWorkflow =>
        services.AddCentraWorkflow<TWorkflow>();

    public static IServiceCollection AddWorkflowActivity<TActivity>(this IServiceCollection services)
        where TActivity : class =>
        services.AddCentraWorkflowActivity<TActivity>();
}
