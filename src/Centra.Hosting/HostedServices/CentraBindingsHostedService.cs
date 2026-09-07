using Centra.Bindings;
using Centra.Hosting.Routing;
using Centra.Locks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Hosting.HostedServices;

public sealed class CentraBindingsHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IScheduler _scheduler;
    private readonly CentraInputBindingDispatcher _dispatcher;
    private readonly ILogger<CentraBindingsHostedService>? _logger;

    public CentraBindingsHostedService(
        IServiceProvider serviceProvider,
        IScheduler scheduler,
        CentraInputBindingDispatcher dispatcher,
        ILogger<CentraBindingsHostedService>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting Centra Bindings and Scheduler runtime...");

        // 1. Activate Cron Jobs
        var cronRegistrations = _serviceProvider.GetServices<CentraCronJobRegistration>();
        var lockProvider = _serviceProvider.GetService<IDistributedLockProvider>();

        foreach (var reg in cronRegistrations)
        {
            var jobHandler = (IJobHandler)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, reg.JobType);

            if (reg.Options?.UseDistributedCoordination != false && lockProvider is not null)
            {
                var lockLogger = _serviceProvider.GetService<ILogger<DistributedJobHandler>>();
                jobHandler = new DistributedJobHandler(jobHandler, lockProvider, logger: lockLogger, lockTimeout: reg.Options?.LockTimeout);
            }

            _scheduler.ScheduleCron(reg.JobName, reg.CronExpression, jobHandler, reg.Options);
            _logger?.LogInformation("Scheduled cron job '{JobName}' ({CronExpression}).", reg.JobName, reg.CronExpression);
        }

        // 2. Register Input Binding Handlers with the Dispatcher
        var bindingRegistrations = _serviceProvider.GetServices<CentraInputBindingRegistration>();
        foreach (var reg in bindingRegistrations)
        {
            _dispatcher.RegisterHandler(reg.BindingName, (data, ct) =>
            {
                using var scope = _serviceProvider.CreateScope();
                var handler = (IBindingTriggerHandler)ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, reg.HandlerType);
                return handler.HandleTriggerAsync(data, ct);
            });

            _logger?.LogInformation("Registered input binding handler for '{BindingName}'.", reg.BindingName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Stopping Centra Bindings and Scheduler runtime...");

        if (_scheduler is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return Task.CompletedTask;
    }
}
