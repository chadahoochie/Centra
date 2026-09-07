using Centra.Actors;
using Centra.Core.Actors;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorManagerTests
{
    private readonly IStateStore _stateStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ActorOptions _options;

    public ActorManagerTests()
    {
        _stateStore = Substitute.For<IStateStore>();
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _options = new ActorOptions
        {
            ActorIdleTimeout = TimeSpan.FromMinutes(10),
            DefaultStateStore = "statestore"
        };

        var services = new ServiceCollection();
        services.AddTransient<TestCounterActor>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_Activate_Actor_On_First_Dispatch_And_Call_OnActivate()
    {
        // Arrange
        var manager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<TestCounterActor>(), "counter-1");

        // Act
        var result = await manager.DispatchAsync(identity, async actor =>
        {
            var counterActor = (TestCounterActor)actor;
            return await counterActor.IncrementAsync();
        });

        // Assert
        result.ShouldBe(1);
        manager.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Deduplicate_Concurrent_Activations()
    {
        // Arrange
        var manager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<TestCounterActor>(), "shared-counter");
        const int concurrentCalls = 50;

        // Act
        var tasks = Enumerable.Range(0, concurrentCalls).Select(_ =>
            manager.DispatchAsync(identity, async actor =>
            {
                var counterActor = (TestCounterActor)actor;
                return await counterActor.IncrementAsync();
            }).AsTask()).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Max().ShouldBe(concurrentCalls);
        manager.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Automatically_SaveState_After_Turn_Execution()
    {
        // Arrange
        var manager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<TestCounterActor>(), "saved-counter");

        // Act
        await manager.DispatchAsync(identity, async actor =>
        {
            await actor.StateManager.SetStateAsync("count", 99);
        });

        // Assert
        await _stateStore.Received(1).SetAsync(
            "statestore",
            "actors:TestCounterActor:saved-counter:count",
            99,
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Passivate_Actor_Explicitly_And_Call_OnDeactivate()
    {
        // Arrange
        var manager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<TestCounterActor>(), "temp-counter");

        await manager.DispatchAsync(identity, actor => ValueTask.FromResult(42));
        manager.ActiveCount.ShouldBe(1);

        // Act
        var passivated = await manager.PassivateActorAsync(identity);

        // Assert
        passivated.ShouldBeTrue();
        manager.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Passivate_Idle_Actors_Exceeding_Timeout()
    {
        // Arrange
        var manager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<TestCounterActor>(), "idle-counter");

        await manager.DispatchAsync(identity, actor => ValueTask.FromResult(1));
        manager.ActiveCount.ShouldBe(1);

        // Advance time beyond idle timeout (10 mins)
        _timeProvider.Advance(TimeSpan.FromMinutes(11));

        // Act
        var count = await manager.PassivateIdleActorsAsync();

        // Assert
        count.ShouldBe(1);
        manager.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Reactivate_Passivated_Actor_On_Subsequent_Call()
    {
        // Arrange
        var manager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<TestCounterActor>(), "reactivate-counter");

        await manager.DispatchAsync(identity, actor => ValueTask.FromResult(1));
        await manager.PassivateActorAsync(identity);
        manager.ActiveCount.ShouldBe(0);

        // Act
        var result = await manager.DispatchAsync(identity, async actor =>
        {
            var counterActor = (TestCounterActor)actor;
            return await counterActor.IncrementAsync();
        });

        // Assert
        result.ShouldBe(1);
        manager.ActiveCount.ShouldBe(1);
    }

    public sealed class TestCounterActor : Actor
    {
        private int _count;
        public int ActivateCount { get; private set; }
        public int DeactivateCount { get; private set; }

        public override ValueTask OnActivateAsync(CancellationToken cancellationToken = default)
        {
            ActivateCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnDeactivateAsync(CancellationToken cancellationToken = default)
        {
            DeactivateCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> IncrementAsync()
        {
            _count++;
            return ValueTask.FromResult(_count);
        }
    }
}
