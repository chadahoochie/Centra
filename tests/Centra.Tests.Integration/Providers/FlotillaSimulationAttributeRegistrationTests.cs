using Centra.Hosting.Extensions;
using Centra.PubSub.Routing;
using Centra.Sample.FlotillaSimulation.Consumer.Handlers;
using Centra.Sample.FlotillaSimulation.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class FlotillaSimulationAttributeRegistrationTests
{
    [Fact]
    public void Should_AutoRegister_Consumer_And_ServiceClient_Via_Attributes_Only()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(options =>
        {
            options.AppId = "flotilla-consumer";
            options.DefaultPubSub = "pubsub";
        }, typeof(TelemetryEventHandler).Assembly, typeof(ITelemetryApiClient).Assembly);

        var sp = services.BuildServiceProvider();

        // 1. Verify ITelemetryApiClient was registered via [ServiceClient("flotilla-api")]
        var client = sp.GetService<ITelemetryApiClient>();
        client.ShouldNotBeNull();

        // 2. Verify TelemetryEventHandler was registered via [Topic("pubsub", "telemetry.v1")]
        var topicRegistrations = sp.GetServices<CentraTopicRegistration>()
            .Where(r => r.HandlerType == typeof(TelemetryEventHandler))
            .ToList();

        topicRegistrations.Count.ShouldBe(1);
        topicRegistrations[0].PubSubName.ShouldBe("pubsub");
        topicRegistrations[0].Topic.ShouldBe("telemetry.v1");

        // 3. Verify handler resolves cleanly with all auto-registered dependencies
        var handler = sp.GetService<TelemetryEventHandler>();
        handler.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Start_Consumer_HostedServices_Without_StateStore_Errors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(options =>
        {
            options.AppId = "flotilla-consumer";
            options.DefaultPubSub = "pubsub";
        }, typeof(TelemetryEventHandler).Assembly, typeof(ITelemetryApiClient).Assembly);

        var sp = services.BuildServiceProvider();
        var hostedServices = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        await Task.Delay(50);

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }
}
