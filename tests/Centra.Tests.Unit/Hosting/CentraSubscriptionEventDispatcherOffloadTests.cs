using System.Text;
using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.PubSub.HostedServices;
using Centra.PubSub.Routing;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Tests.Unit.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraSubscriptionEventDispatcherOffloadTests
{
    [Fact]
    public async Task DispatchEventAsync_NormalTenant_ProcessesNormallyAndRecordsMetrics()
    {
        var services = new ServiceCollection();
        var handler = new DummyTenantEventHandler();
        services.AddSingleton(handler);

        var options = new TenantOffloadOptions();
        var tracker = new RollingWindowTenantMetricsTracker(options);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        services.AddSingleton<ITenantOffloadCoordinator>(coordinator);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyTenantEvent),
            typeof(DummyTenantEventHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => ((DummyTenantEventHandler)h).HandleAsync(new DummyTenantEvent("1"), default, ct));

        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var payload = Encoding.UTF8.GetBytes("""{"id":"1"}""");
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.TenantIdHeader] = "tenant-normal"
        };

        var result = await dispatcher.DispatchEventAsync(reg, payload, headers, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);
        Assert.True(handler.Handled);

        var stats = tracker.GetTenantStats("tenant-normal", "orders");
        Assert.Equal(1, stats.MessageCount);
    }

    [Fact]
    public async Task DispatchEventAsync_OffloadedTenant_InterceptsAndRoutesToOffloadStrategy()
    {
        var services = new ServiceCollection();
        var handler = new DummyTenantEventHandler();
        services.AddSingleton(handler);

        var options = new TenantOffloadOptions
        {
            OffloadStrategy = TenantOffloadStrategyType.EphemeralBrokerTopic,
            OffloadTopicPattern = "{topic}.offload.{tenantId}"
        };

        var tracker = new RollingWindowTenantMetricsTracker(options);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        // Force offload tenant-noisy
        coordinator.ForceOffload("tenant-noisy", "orders", TenantOffloadReason.HighTrafficShare);

        services.AddSingleton<ITenantOffloadCoordinator>(coordinator);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyTenantEvent),
            typeof(DummyTenantEventHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => ((DummyTenantEventHandler)h).HandleAsync(new DummyTenantEvent("1"), default, ct));

        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var payload = Encoding.UTF8.GetBytes("""{"id":"1"}""");
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.TenantIdHeader] = "tenant-noisy"
        };

        var result = await dispatcher.DispatchEventAsync(reg, payload, headers, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);

        // In EphemeralBrokerTopic mode, handler is not called locally; message is forwarded to offload topic
        Assert.False(handler.Handled);
        Assert.Single(publisher.PublishedMessages);
        Assert.Equal("orders.offload.tenant-noisy", publisher.PublishedMessages[0].Topic);
    }

    [Fact]
    public async Task DispatchEventAsync_AlreadyOffloadedMessage_ExecutesDirectlyWithoutReoffloading()
    {
        var services = new ServiceCollection();
        var handler = new DummyTenantEventHandler();
        services.AddSingleton(handler);

        var options = new TenantOffloadOptions();
        var tracker = new RollingWindowTenantMetricsTracker(options);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        coordinator.ForceOffload("tenant-noisy", "orders", TenantOffloadReason.HighTrafficShare);

        services.AddSingleton<ITenantOffloadCoordinator>(coordinator);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyTenantEvent),
            typeof(DummyTenantEventHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => ((DummyTenantEventHandler)h).HandleAsync(new DummyTenantEvent("1"), default, ct));

        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var payload = Encoding.UTF8.GetBytes("""{"id":"1"}""");
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.TenantIdHeader] = "tenant-noisy",
            ["ce-offloaded"] = "true" // Already offloaded message received on offload topic
        };

        var result = await dispatcher.DispatchEventAsync(reg, payload, headers, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);
        Assert.True(handler.Handled);
        Assert.Empty(publisher.PublishedMessages); // Did not publish again!
    }

    [Fact]
    public async Task DispatchEventAsync_WhenOffloadedInProcess_ExecutesThroughPipeline()
    {
        var services = new ServiceCollection();
        var handler = new DummyTenantEventHandler();
        services.AddSingleton(handler);

        var options = new TenantOffloadOptions
        {
            OffloadStrategy = TenantOffloadStrategyType.InProcessFairScheduler
        };

        var tracker = new RollingWindowTenantMetricsTracker(options);
        await using var strategy = new InProcessFairSchedulerOffloadStrategy(options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        coordinator.ForceOffload("tenant-fair", "orders", TenantOffloadReason.HighTrafficShare);

        services.AddSingleton<ITenantOffloadCoordinator>(coordinator);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyTenantEvent),
            typeof(DummyTenantEventHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => ((DummyTenantEventHandler)h).HandleAsync(new DummyTenantEvent("1"), default, ct));

        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var payload = Encoding.UTF8.GetBytes("""{"id":"1"}""");
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.TenantIdHeader] = "tenant-fair"
        };

        var result = await dispatcher.DispatchEventAsync(reg, payload, headers, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);
        Assert.True(handler.Handled);
        Assert.Null(CentraAmbientContext.TenantId);
    }

    [Fact]
    public async Task DispatchEventAsync_WhenOffloadThrows_CleansUpAmbientContext()
    {
        var services = new ServiceCollection();
        var handler = new DummyTenantEventHandler();
        services.AddSingleton(handler);

        var options = new TenantOffloadOptions();
        var tracker = new RollingWindowTenantMetricsTracker(options);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        coordinator.ForceOffload("tenant-err", "orders", TenantOffloadReason.HighTrafficShare);

        services.AddSingleton<ITenantOffloadCoordinator>(coordinator);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyTenantEvent),
            typeof(DummyTenantEventHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => throw new InvalidOperationException("Boom"));

        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var payload = Encoding.UTF8.GetBytes("""{"id":"1"}""");
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.TenantIdHeader] = "tenant-err"
        };

        var result = await dispatcher.DispatchEventAsync(reg, payload, headers, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result); // Ephemeral published to offload topic successfully
        Assert.Null(CentraAmbientContext.TenantId);
    }

    [Fact]
    public void CentraAttributeScanner_WhenEnableTenantOffloadIsTrue_RegistersShardTopics()
    {
        var services = new ServiceCollection();
        Centra.Hosting.Discovery.CentraAttributeScanner.ScanAndRegister(services, [typeof(TestOffloadHandler).Assembly]);

        var registrations = services
            .Where(s => s.ServiceType == typeof(CentraTopicRegistration))
            .Select(s => s.ImplementationInstance as CentraTopicRegistration)
            .Where(r => r is not null)
            .ToList();

        Assert.Contains(registrations, r => r!.Topic == "offload-topic");
        Assert.Contains(registrations, r => r!.Topic == "offload-topic.offload.0");
        Assert.Contains(registrations, r => r!.Topic == "offload-topic.offload.1");
        Assert.Contains(registrations, r => r!.Topic == "offload-topic.offload.2");
        Assert.Contains(registrations, r => r!.Topic == "offload-topic.offload.3");
    }
}
