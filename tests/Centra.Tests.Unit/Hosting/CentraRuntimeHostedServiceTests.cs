using Centra.Drivers;
using Centra.Events;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.Registry;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraRuntimeHostedServiceTests
{
    private sealed class TestEvent;

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public bool Handled { get; private set; }
        public Task<EventHandlingResult> HandleAsync(TestEvent @event, EventContext context, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.FromResult(EventHandlingResult.Success);
        }
    }

    private sealed class FailingEventHandler : IEventHandler<TestEvent>
    {
        public Task<EventHandlingResult> HandleAsync(TestEvent @event, EventContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler exploded");
        }
    }

    [Fact]
    public async Task StartAsync_SubscribesDrivers_And_DispatchesEventsSuccessfully()
    {
        // Arrange
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IPubSubDriver>();
        registry.RegisterPubSubDriver("default-bus", mockDriver);

        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>? capturedCallback = null;

        mockDriver.SubscribeAsync(
            "default-bus",
            "test.topic",
            Arg.Any<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<PubSubSubscribeOptions?>())
            .Returns(callInfo =>
            {
                capturedCallback = callInfo.Arg<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>();
                return ValueTask.CompletedTask;
            });

        var services = new ServiceCollection();
        var handlerInstance = new TestEventHandler();
        services.AddSingleton<TestEventHandler>(handlerInstance);
        var serviceProvider = services.BuildServiceProvider();

        var registration = new CentraTopicRegistration(
            "default-bus",
            "test.topic",
            typeof(TestEvent),
            typeof(TestEventHandler),
            invoker: (h, bytes, hdrs, ct) => Task.FromResult(EventHandlingResult.Success));

        var options = Options.Create(new CentraOptions { AppId = "test-app" });
        var hostedService = new CentraRuntimeHostedService(
            registry,
            serviceProvider,
            options,
            [registration],
            NullLogger<CentraRuntimeHostedService>.Instance);

        // Act - Start
        await hostedService.StartAsync(CancellationToken.None);

        capturedCallback.ShouldNotBeNull();

        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.CorrelationIdHeader] = "corr-1",
            [CloudEventConstants.IdHeader] = "msg-1",
            [CloudEventConstants.TenantIdHeader] = "tenant-1"
        };

        var result = await capturedCallback(ReadOnlyMemory<byte>.Empty, headers, CancellationToken.None);
        result.ShouldBe(EventHandlingResult.Success);

        // Act - Stop
        await hostedService.StopAsync(CancellationToken.None);
        await mockDriver.Received(1).UnsubscribeAsync("default-bus", "test.topic", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_HandlerNotResolvedInDI_ReturnsDrop()
    {
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IPubSubDriver>();
        registry.RegisterPubSubDriver("bus1", mockDriver);

        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>? capturedCallback = null;

        mockDriver.SubscribeAsync(
            "bus1",
            "orders",
            Arg.Any<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<PubSubSubscribeOptions?>())
            .Returns(callInfo =>
            {
                capturedCallback = callInfo.Arg<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>();
                return ValueTask.CompletedTask;
            });

        // Empty service collection (TestEventHandler not registered)
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var registration = new CentraTopicRegistration(
            "bus1",
            "orders",
            typeof(TestEvent),
            typeof(TestEventHandler));

        var hostedService = new CentraRuntimeHostedService(
            registry,
            serviceProvider,
            Options.Create(new CentraOptions { AppId = "test-app" }),
            [registration],
            NullLogger<CentraRuntimeHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        capturedCallback.ShouldNotBeNull();

        var result = await capturedCallback(ReadOnlyMemory<byte>.Empty, new Dictionary<string, string>(), CancellationToken.None);
        result.ShouldBe(EventHandlingResult.Drop);
    }

    [Fact]
    public async Task StartAsync_HandlerThrows_ReturnsDeadLetter()
    {
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IPubSubDriver>();
        registry.RegisterPubSubDriver("bus2", mockDriver);

        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>? capturedCallback = null;

        mockDriver.SubscribeAsync(
            "bus2",
            "faults",
            Arg.Any<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<PubSubSubscribeOptions?>())
            .Returns(callInfo =>
            {
                capturedCallback = callInfo.Arg<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>();
                return ValueTask.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddTransient<FailingEventHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var registration = new CentraTopicRegistration(
            "bus2",
            "faults",
            typeof(TestEvent),
            typeof(FailingEventHandler),
            invoker: (h, _, _, _) => throw new InvalidOperationException("Invoker fail"));

        var hostedService = new CentraRuntimeHostedService(
            registry,
            serviceProvider,
            Options.Create(new CentraOptions { AppId = "test-app" }),
            [registration],
            NullLogger<CentraRuntimeHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        capturedCallback.ShouldNotBeNull();

        var result = await capturedCallback(ReadOnlyMemory<byte>.Empty, new Dictionary<string, string>(), CancellationToken.None);
        result.ShouldBe(EventHandlingResult.DeadLetter);
    }

    [Fact]
    public async Task StartAsync_ResilienceProviderRegistered_ExecutesThroughPipeline()
    {
        var registry = new ComponentRegistry();
        var mockDriver = Substitute.For<IPubSubDriver>();
        registry.RegisterPubSubDriver("resilient-bus", mockDriver);

        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>? capturedCallback = null;

        mockDriver.SubscribeAsync(
            "resilient-bus",
            "resilient.topic",
            Arg.Any<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<PubSubSubscribeOptions?>())
            .Returns(callInfo =>
            {
                capturedCallback = callInfo.Arg<Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>>>();
                return ValueTask.CompletedTask;
            });

        var resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        var pipeline = Substitute.For<IResiliencePipeline>();

        pipeline.ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask<EventHandlingResult>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var callback = callInfo.Arg<Func<CancellationToken, ValueTask<EventHandlingResult>>>();
                return callback(CancellationToken.None);
            });

        resilienceProvider.GetPubSubPipeline("resilient-bus").Returns(pipeline);

        var services = new ServiceCollection();
        services.AddTransient<TestEventHandler>();
        services.AddSingleton(resilienceProvider);
        var serviceProvider = services.BuildServiceProvider();

        var registration = new CentraTopicRegistration(
            "resilient-bus",
            "resilient.topic",
            typeof(TestEvent),
            typeof(TestEventHandler),
            invoker: (h, _, _, _) => Task.FromResult(EventHandlingResult.Success));

        var hostedService = new CentraRuntimeHostedService(
            registry,
            serviceProvider,
            Options.Create(new CentraOptions { AppId = "test-app" }),
            [registration],
            NullLogger<CentraRuntimeHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        capturedCallback.ShouldNotBeNull();

        var result = await capturedCallback(ReadOnlyMemory<byte>.Empty, new Dictionary<string, string>(), CancellationToken.None);
        result.ShouldBe(EventHandlingResult.Success);
        resilienceProvider.Received(1).GetPubSubPipeline("resilient-bus");
    }

    [Fact]
    public async Task StartAsync_MissingDriver_SkipsRegistration()
    {
        var registry = new ComponentRegistry(); // No drivers registered
        var registration = new CentraTopicRegistration(
            "nonexistent-bus",
            "topic1",
            typeof(TestEvent),
            typeof(TestEventHandler));

        var hostedService = new CentraRuntimeHostedService(
            registry,
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new CentraOptions { AppId = "test-app" }),
            [registration],
            NullLogger<CentraRuntimeHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);
    }
}
