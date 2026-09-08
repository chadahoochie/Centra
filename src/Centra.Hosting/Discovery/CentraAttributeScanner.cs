using System.Reflection;
using Centra.Actors;
using Centra.Bindings;
using Centra.Invocation;
using Centra.PubSub;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.Hosting.Discovery;

/// <summary>
/// Discovers attribute-decorated components ([Topic], [Workflow], [WorkflowActivity],
/// [CronBinding], [Binding], [ServiceClient], [Actor]) in the given assemblies and registers
/// them by reflectively invoking the same per-concern Add* helper an explicit call would use, so
/// there is exactly one source of truth for what "registering X" means. Registration helpers are
/// idempotent (see <see cref="ServiceCollectionRegistrationExtensions.HasRegistrationFor{T}"/>),
/// so an explicit call for a type the scanner also finds is not duplicated - the explicit call's
/// values win because it is what already populated the registration list.
/// </summary>
internal static class CentraAttributeScanner
{
    public static void ScanAndRegister(IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                RegisterIfEventHandler(services, type);
                RegisterIfWorkflow(services, type);
                RegisterIfWorkflowActivity(services, type);
                RegisterIfCronJob(services, type);
                RegisterIfInputBindingHandler(services, type);
                RegisterIfServiceClient(services, type);
                RegisterIfActor(services, type);
            }
        }
    }

    private static void RegisterIfEventHandler(IServiceCollection services, Type type)
    {
        if (type.IsAbstract || type.GetCustomAttribute<TopicAttribute>() is null)
        {
            return;
        }

        var eventHandlerInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));
        if (eventHandlerInterface is null)
        {
            return;
        }

        var eventType = eventHandlerInterface.GetGenericArguments()[0];
        var method = typeof(Extensions.CentraPubSubServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraPubSubServiceCollectionExtensions.AddCentraEventHandler))!
            .MakeGenericMethod(type, eventType);

        method.Invoke(null, [services, null, null, null]);
    }

    private static void RegisterIfWorkflow(IServiceCollection services, Type type)
    {
        if (type.IsAbstract || type.GetCustomAttribute<WorkflowAttribute>() is null || !typeof(IWorkflow).IsAssignableFrom(type))
        {
            return;
        }

        var method = typeof(Extensions.CentraWorkflowServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraWorkflowServiceCollectionExtensions.AddCentraWorkflow))!
            .MakeGenericMethod(type);

        method.Invoke(null, [services]);
    }

    private static void RegisterIfWorkflowActivity(IServiceCollection services, Type type)
    {
        if (type.IsAbstract || type.GetCustomAttribute<WorkflowActivityAttribute>() is null)
        {
            return;
        }

        var method = typeof(Extensions.CentraWorkflowServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraWorkflowServiceCollectionExtensions.AddCentraWorkflowActivity))!
            .MakeGenericMethod(type);

        method.Invoke(null, [services]);
    }

    private static void RegisterIfCronJob(IServiceCollection services, Type type)
    {
        if (type.IsAbstract || !typeof(IJobHandler).IsAssignableFrom(type))
        {
            return;
        }

        var executeMethod = type.GetMethod(nameof(IJobHandler.ExecuteAsync));
        if (executeMethod?.GetCustomAttribute<CronBindingAttribute>() is null)
        {
            return;
        }

        var method = typeof(Extensions.CentraBindingsServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraBindingsServiceCollectionExtensions.AddCentraCronJob))!
            .MakeGenericMethod(type);

        method.Invoke(null, [services, null, null, null]);
    }

    private static void RegisterIfInputBindingHandler(IServiceCollection services, Type type)
    {
        if (type.IsAbstract || !typeof(IBindingTriggerHandler).IsAssignableFrom(type))
        {
            return;
        }

        var handleMethod = type.GetMethod(nameof(IBindingTriggerHandler.HandleTriggerAsync));
        if (handleMethod?.GetCustomAttribute<BindingAttribute>() is null)
        {
            return;
        }

        var method = typeof(Extensions.CentraBindingsServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraBindingsServiceCollectionExtensions.AddCentraInputBindingHandler))!
            .MakeGenericMethod(type);

        method.Invoke(null, [services, null]);
    }

    private static void RegisterIfServiceClient(IServiceCollection services, Type type)
    {
        if (!type.IsInterface || type.GetCustomAttribute<ServiceClientAttribute>() is null)
        {
            return;
        }

        var method = typeof(Extensions.CentraInvocationServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraInvocationServiceCollectionExtensions.AddCentraServiceClient))!
            .MakeGenericMethod(type);

        method.Invoke(null, [services]);
    }

    private static void RegisterIfActor(IServiceCollection services, Type type)
    {
        if (type.IsAbstract || type.GetCustomAttribute<ActorAttribute>() is null || !typeof(Actor).IsAssignableFrom(type))
        {
            return;
        }

        var actorInterfaces = type.GetInterfaces()
            .Where(i => typeof(IActor).IsAssignableFrom(i) && i != typeof(IActor))
            .ToArray();

        if (actorInterfaces.Length != 1)
        {
            throw new InvalidOperationException(
                $"'{type.Name}' is decorated with [Actor] but implements {actorInterfaces.Length} domain interfaces derived from IActor; " +
                "expected exactly one. Register it explicitly with AddCentraActor<TActor, TActorInterface>() instead.");
        }

        var method = typeof(Extensions.CentraActorServiceCollectionExtensions)
            .GetMethod(nameof(Extensions.CentraActorServiceCollectionExtensions.AddCentraActor))!
            .MakeGenericMethod(type, actorInterfaces[0]);

        method.Invoke(null, [services]);
    }
}
