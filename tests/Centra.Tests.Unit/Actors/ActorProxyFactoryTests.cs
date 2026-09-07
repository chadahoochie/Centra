using System.Diagnostics;
using Centra.Actors;
using Centra.Core.Actors;
using Centra.Diagnostics;
using Centra.Invocation;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorProxyFactoryTests
{
    private readonly IStateStore _stateStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ActorManager _actorManager;
    private readonly IActorPlacementDirector _placementDirector;
    private readonly IServiceInvoker _serviceInvoker;
    private readonly ActorProxyFactory _proxyFactory;

    public ActorProxyFactoryTests()
    {
        _stateStore = Substitute.For<IStateStore>();
        _placementDirector = Substitute.For<IActorPlacementDirector>();
        _serviceInvoker = Substitute.For<IServiceInvoker>();

        var services = new ServiceCollection();
        services.AddTransient<CalculatorActor>();
        _serviceProvider = services.BuildServiceProvider();

        var options = new ActorOptions { DefaultStateStore = "statestore" };
        _actorManager = new ActorManager(_serviceProvider, _stateStore, options);

        _proxyFactory = new ActorProxyFactory(_actorManager, _placementDirector, _serviceInvoker);
    }

    [Fact]
    public async Task Should_Create_Typed_Actor_Proxy_And_Dispatch_Local_Method_Call()
    {
        // Arrange
        var actorId = new ActorId("calc-1");
        var identity = new ActorIdentity("CalculatorActor", actorId);

        _placementDirector.IsLocal(identity).Returns(true);

        var proxy = _proxyFactory.CreateActorProxy<ICalculatorActor>(actorId);

        // Act
        var sum = await proxy.AddAsync(15, 27);

        // Assert
        sum.ShouldBe(42);
        _actorManager.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Dispatch_ValueTask_Method_Call()
    {
        // Arrange
        var actorId = new ActorId("calc-val");
        var identity = new ActorIdentity("CalculatorActor", actorId);

        _placementDirector.IsLocal(identity).Returns(true);

        var proxy = _proxyFactory.CreateActorProxy<ICalculatorActor>(actorId);

        // Act
        var result = await proxy.MultiplyAsync(6, 7);

        // Assert
        result.ShouldBe(42);
    }

    [Fact]
    public async Task Should_Dispatch_Void_ValueTask_Method_Call()
    {
        // Arrange
        var actorId = new ActorId("calc-void");
        var identity = new ActorIdentity("CalculatorActor", actorId);

        _placementDirector.IsLocal(identity).Returns(true);

        var proxy = _proxyFactory.CreateActorProxy<ICalculatorActor>(actorId);

        // Act
        await proxy.ResetAsync();

        // Assert
        _actorManager.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Route_To_Remote_ServiceInvoker_When_Placement_Is_Non_Local()
    {
        // Arrange
        var actorId = new ActorId("calc-remote");
        var identity = new ActorIdentity("CalculatorActor", actorId);

        _placementDirector.IsLocal(identity).Returns(false);
        _placementDirector.ResolveNodeId(identity).Returns("node-east-1");

        _serviceInvoker.InvokeMethodAsync<object, int>(
            "node-east-1",
            "centra/actors/CalculatorActor/calc-remote/method/AddAsync",
            Arg.Any<object>(),
            "POST",
            Arg.Any<ServiceInvocationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(999));

        var proxy = _proxyFactory.CreateActorProxy<ICalculatorActor>(actorId);

        // Act
        var result = await proxy.AddAsync(100, 200);

        // Assert
        result.ShouldBe(999);
        _actorManager.ActiveCount.ShouldBe(0); // Not activated locally!
    }

    [Fact]
    public async Task Should_Propagate_Exceptions_From_Actor_Method_Cleanly()
    {
        // Arrange
        var actorId = new ActorId("calc-err");
        var identity = new ActorIdentity("CalculatorActor", actorId);

        _placementDirector.IsLocal(identity).Returns(true);

        var proxy = _proxyFactory.CreateActorProxy<ICalculatorActor>(actorId);

        // Act & Assert
        await Should.ThrowAsync<DivideByZeroException>(async () => await proxy.DivideAsync(10, 0));
    }

    [Fact]
    public async Task Should_Emit_OpenTelemetry_Client_Activity_Span()
    {
        // Arrange
        var actorId = new ActorId("calc-trace");
        var identity = new ActorIdentity("CalculatorActor", actorId);
        _placementDirector.IsLocal(identity).Returns(true);

        var proxy = _proxyFactory.CreateActorProxy<ICalculatorActor>(actorId);

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Centra",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act =>
            {
                if (act.OperationName == "Actor.Invoke")
                {
                    captured = act;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        await proxy.AddAsync(1, 2);

        // Assert
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(ActivityKind.Client);
        captured.GetTagItem("actor.type")?.ToString().ShouldBe("CalculatorActor");
        captured.GetTagItem("actor.id")?.ToString().ShouldBe("calc-trace");
        captured.GetTagItem("actor.method")?.ToString().ShouldBe("AddAsync");
    }

    public interface ICalculatorActor : IActor
    {
        Task<int> AddAsync(int a, int b);
        ValueTask<int> MultiplyAsync(int a, int b);
        ValueTask ResetAsync();
        Task<int> DivideAsync(int a, int b);
    }

    public sealed class CalculatorActor : Actor, ICalculatorActor
    {
        public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
        public ValueTask<int> MultiplyAsync(int a, int b) => ValueTask.FromResult(a * b);
        public ValueTask ResetAsync() => ValueTask.CompletedTask;
        public Task<int> DivideAsync(int a, int b)
        {
            if (b == 0) throw new DivideByZeroException("Cannot divide by zero.");
            return Task.FromResult(a / b);
        }
    }
}
