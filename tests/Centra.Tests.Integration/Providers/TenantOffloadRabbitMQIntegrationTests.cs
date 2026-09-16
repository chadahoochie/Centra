using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.RabbitMq;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class TenantOffloadRabbitMQIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.3-management")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Should_Distribute_Tenant_Traffic_Across_Multiple_Instances_Via_RabbitMQ()
    {
        var connectionString = _container.GetConnectionString();

        var state1 = new TenantConsumerNodeState("node-alpha", "RabbitMQ");
        var state2 = new TenantConsumerNodeState("node-beta", "RabbitMQ");

        var host1 = Host.CreateDefaultBuilder()
            .ConfigureLogging(static logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddCentra(static options =>
                {
                    options.AppId = "tenant-offload-cluster";
                    options.DefaultPubSub = "pubsub";
                    options.ControlPlane.InstanceId = "node-alpha";
                }, typeof(TenantOrderEventHandler).Assembly);

                services.AddCentraRabbitMQPubSub("pubsub", options =>
                {
                    options.ConnectionString = connectionString;
                    options.QueuePrefix = "centra-tenant-test";
                });

                services.AddCentraTenantOffload(static options =>
                {
                    options.WindowDuration = TimeSpan.FromSeconds(5);
                    options.MinSampleCount = 5;
                    options.TrafficShareThreshold = 0.50;
                    options.DurationMultiplierThreshold = 2.0;
                    options.OffloadStrategy = TenantOffloadStrategyType.InProcessFairScheduler;
                });

                services.AddSingleton<ITenantConsumerNodeState>(state1);
            })
            .Build();

        var host2 = Host.CreateDefaultBuilder()
            .ConfigureLogging(static logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddCentra(static options =>
                {
                    options.AppId = "tenant-offload-cluster";
                    options.DefaultPubSub = "pubsub";
                    options.ControlPlane.InstanceId = "node-beta";
                }, typeof(TenantOrderEventHandler).Assembly);

                services.AddCentraRabbitMQPubSub("pubsub", options =>
                {
                    options.ConnectionString = connectionString;
                    options.QueuePrefix = "centra-tenant-test";
                });

                services.AddCentraTenantOffload(static options =>
                {
                    options.WindowDuration = TimeSpan.FromSeconds(5);
                    options.MinSampleCount = 5;
                    options.TrafficShareThreshold = 0.50;
                    options.DurationMultiplierThreshold = 2.0;
                    options.OffloadStrategy = TenantOffloadStrategyType.InProcessFairScheduler;
                });

                services.AddSingleton<ITenantConsumerNodeState>(state2);
            })
            .Build();

        await host1.StartAsync();
        await host2.StartAsync();

        try
        {
            var publisher = host1.Services.GetRequiredService<IPubSubClient>();
            var totalMessages = 30;

            for (var i = 1; i <= totalMessages; i++)
            {
                var tenant = (i % 2 == 0) ? "tenant-alpha" : "tenant-beta";
                var order = new TenantOrderEvent($"ord-it-{i}", tenant, 100m + i, $"Order #{i}");
                var options = new PubSubPublishOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        [CloudEventConstants.TenantIdHeader] = tenant
                    }
                };

                await publisher.PublishAsync("tenant.orders", order, options);
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && (state1.TotalHandled + state2.TotalHandled < totalMessages))
            {
                await Task.Delay(100);
            }

            (state1.TotalHandled + state2.TotalHandled).ShouldBe(totalMessages);
            state1.TotalHandled.ShouldBeGreaterThan(0);
            state2.TotalHandled.ShouldBeGreaterThan(0);
            state1.TenantHandledCounts.ShouldNotBeEmpty();
            state2.TenantHandledCounts.ShouldNotBeEmpty();
        }
        finally
        {
            await host1.StopAsync();
            await host2.StopAsync();
            host1.Dispose();
            host2.Dispose();
        }
    }
}
