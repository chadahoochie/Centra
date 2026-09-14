using System.Diagnostics;
using System.Reflection;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.PubSub.Inbox;
using Centra.Registry;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.HostedServices;

public sealed class CentraRuntimeHostedService : IHostedService
{
    private readonly ComponentRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CentraOptions> _options;
    private readonly IReadOnlyList<CentraTopicRegistration> _registrations;
    private readonly CentraTopicRouterRegistry _routerRegistry;
    private readonly ILogger<CentraRuntimeHostedService> _logger;
    private readonly CentraSubscriptionEventDispatcher _dispatcher;

    public CentraRuntimeHostedService(
        ComponentRegistry registry,
        IServiceProvider serviceProvider,
        IOptions<CentraOptions> options,
        IEnumerable<CentraTopicRegistration> registrations,
        ILogger<CentraRuntimeHostedService> logger)
    {
        _registry = registry;
        _serviceProvider = serviceProvider;
        _options = options;
        var regs = registrations.ToArray();
        _registrations = regs;
        _routerRegistry = new CentraTopicRouterRegistry(regs);
        _logger = logger;
        _dispatcher = new CentraSubscriptionEventDispatcher(serviceProvider, logger);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogCentraInitialized(_options.Value.AppId);

        foreach (var router in _routerRegistry.GetRouters())
        {
            var driver = _registry.GetPubSubDriver(router.PubSubName);
            if (driver is null)
            {
                continue;
            }

            var localRouter = router;
            await driver.SubscribeAsync(
                localRouter.PubSubName,
                localRouter.Topic,
                async (payload, headers, ct) =>
                {
                    return await _dispatcher.DispatchEventAsync(localRouter, payload, headers, ct).ConfigureAwait(false);
                },
                localRouter.DeadLetterTopic,
                cancellationToken,
                localRouter.SubscriptionOptions).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var router in _routerRegistry.GetRouters())
        {
            var driver = _registry.GetPubSubDriver(router.PubSubName);
            if (driver is not null)
            {
                await driver.UnsubscribeAsync(router.PubSubName, router.Topic, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
