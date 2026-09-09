using System.Reflection;
using Centra.Bindings;
using Centra.Drivers;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Centra.Hosting.Extensions;

public static class CentraBindingsServiceCollectionExtensions
{
    public static IServiceCollection AddCentraBindings(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddSingleton<IOutputBinding, CentraOutputBinding>();
        services.TryAddSingleton<CentraInputBindingDispatcher>();
        services.TryAddSingleton<CentraCronScheduler>();
        services.TryAddSingleton<IScheduler>(sp => sp.GetRequiredService<CentraCronScheduler>());
        services.AddHostedService<CentraBindingsHostedService>();

        return services;
    }

    public static IServiceCollection AddCentraCronJob<TJob>(
        this IServiceCollection services,
        string? jobName = null,
        string? cronExpression = null,
        CronScheduleOptions? options = null)
        where TJob : class, IJobHandler
    {
        if (jobName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        }
        if (cronExpression is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        }

        services.TryAddTransient<TJob>();

        var method = typeof(TJob).GetMethod(nameof(IJobHandler.ExecuteAsync));
        var attr = method?.GetCustomAttribute<CronBindingAttribute>();

        var resolvedName = jobName ?? typeof(TJob).Name;
        var resolvedCron = cronExpression ?? attr?.CronExpression
            ?? throw new InvalidOperationException(
                $"'{typeof(TJob).Name}' has no [CronBinding] on {nameof(IJobHandler.ExecuteAsync)} and no cronExpression was supplied.");
        var resolvedOptions = options ?? (attr is not null
            ? new CronScheduleOptions(MissedRunBehavior: attr.MissedRunBehavior)
            : null);

        services.ReplaceRegistrationFor<CentraCronJobRegistration>(
            r => r.JobType == typeof(TJob),
            new CentraCronJobRegistration(resolvedName, resolvedCron, typeof(TJob), resolvedOptions));

        return services;
    }

    public static IServiceCollection AddCentraInputBindingHandler<THandler>(
        this IServiceCollection services,
        string? bindingName = null)
        where THandler : class, IBindingTriggerHandler
    {
        if (bindingName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        }

        services.TryAddTransient<THandler>();

        var method = typeof(THandler).GetMethod(nameof(IBindingTriggerHandler.HandleTriggerAsync));
        var attr = method?.GetCustomAttribute<BindingAttribute>();

        var resolvedName = bindingName ?? attr?.BindingName
            ?? throw new InvalidOperationException(
                $"'{typeof(THandler).Name}' has no [Binding] on {nameof(IBindingTriggerHandler.HandleTriggerAsync)} and no bindingName was supplied.");

        services.ReplaceRegistrationFor<CentraInputBindingRegistration>(
            r => r.HandlerType == typeof(THandler),
            new CentraInputBindingRegistration(resolvedName, typeof(THandler)));

        return services;
    }

    public static IServiceCollection AddCentraHttpWebhookBinding(
        this IServiceCollection services,
        string bindingName,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        services.AddHttpClient();
        services.AddSingleton<IBindingDriver>(sp =>
        {
            var client = httpClient ?? sp.GetRequiredService<IHttpClientFactory>().CreateClient($"binding:{bindingName}");
            var logger = sp.GetService<ILogger<HttpWebhookBindingDriver>>();
            var driver = new HttpWebhookBindingDriver(client, logger);

            var registry = sp.GetRequiredService<ComponentRegistry>();
            registry.RegisterBindingDriver(bindingName, driver);

            return driver;
        });

        return services;
    }
}
