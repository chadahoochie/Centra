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
        string jobName,
        string cronExpression,
        CronScheduleOptions? options = null)
        where TJob : class, IJobHandler
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        services.TryAddTransient<TJob>();
        services.AddSingleton(new CentraCronJobRegistration(jobName, cronExpression, typeof(TJob), options));

        return services;
    }

    public static IServiceCollection AddCentraInputBindingHandler<THandler>(
        this IServiceCollection services,
        string bindingName)
        where THandler : class, IBindingTriggerHandler
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        services.TryAddTransient<THandler>();
        services.AddSingleton(new CentraInputBindingRegistration(bindingName, typeof(THandler)));

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
