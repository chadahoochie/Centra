using Centra.Core.Workflows;
using Centra.Locks;
using Centra.Serialization;
using Centra.State;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class DurableWorkflowTimerTests
{
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly ICentraSerializer _serializer = new JsonCentraSerializer();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceCollection _services = new();

    public sealed class SampleTimerWorkflow : Workflow<string, string>
    {
        public override async ValueTask<string> RunAsync(IWorkflowContext context, string input)
        {
            await context.CreateTimerAsync(TimeSpan.FromMinutes(10));
            return $"Finished after timer: {input}";
        }
    }

    [Fact]
    public async Task Should_Persist_Timer_When_Workflow_Suspends_On_Timer()
    {
        var db = new Dictionary<string, object>();
        var timerStore = new InMemoryDurableWorkflowTimerStore();
        var (engine, registry, _) = DurableWorkflowTestEngineFactory.Create(_stateStore, db, timerStore, _serializer, _timeProvider, _services.BuildServiceProvider());

        registry.RegisterWorkflow(new WorkflowDefinition("SampleTimer", typeof(SampleTimerWorkflow), typeof(string), typeof(string)));

        var instanceId = await engine.StartWorkflowAsync("SampleTimer", "test-order");

        var due = await timerStore.GetDueTimersAsync(_timeProvider.GetUtcNow().AddMinutes(15));
        due.Count.ShouldBe(1);
        due[0].InstanceId.ShouldBe(instanceId);
        due[0].DueTimeUtc.ShouldBe(_timeProvider.GetUtcNow().AddMinutes(10));
    }

    [Fact]
    public async Task Should_Recover_And_Fire_Timer_On_Coordinator_Sweep()
    {
        var db = new Dictionary<string, object>();
        var timerStore = new InMemoryDurableWorkflowTimerStore();
        var (engine, registry, historyStore) = DurableWorkflowTestEngineFactory.Create(_stateStore, db, timerStore, _serializer, _timeProvider, _services.BuildServiceProvider());

        registry.RegisterWorkflow(new WorkflowDefinition("SampleTimer", typeof(SampleTimerWorkflow), typeof(string), typeof(string)));

        var instanceId = await engine.StartWorkflowAsync("SampleTimer", "recovered-order");

        // Simulate node crash by disposing old engine (in-memory timer disposed)
        ((IDisposable)engine).Dispose();

        // Fresh node boots up with no in-memory active timers
        var freshScheduler = new WorkflowTimerScheduler(_timeProvider);
        var freshEngine = new WorkflowEngine(
            _services.BuildServiceProvider(),
            registry,
            historyStore,
            Substitute.For<IWorkflowActivityDispatcher>(),
            _serializer,
            _timeProvider,
            timerScheduler: freshScheduler,
            durableTimerStore: timerStore);

        var coordinator = new DurableWorkflowTimerCoordinator(
            timerStore,
            id => freshEngine.FireTimerAsync(id),
            lockProvider: null,
            timeProvider: _timeProvider);

        // Advance past due time
        _timeProvider.Advance(TimeSpan.FromMinutes(11));

        // Sweep coordinator
        var firedCount = await coordinator.ProcessDueTimersAsync();
        firedCount.ShouldBe(1);

        // Verify timer is deleted from store
        var remaining = await timerStore.GetDueTimersAsync(_timeProvider.GetUtcNow().AddHours(1));
        remaining.Count.ShouldBe(0);

        // Verify workflow reached completed status
        var state = await historyStore.GetStateAsync(instanceId);
        state.ShouldNotBeNull();
        state.Status.ShouldBe((int)WorkflowStatus.Completed);
        var output = _serializer.Deserialize<string>(state.Output!);
        output.ShouldBe("Finished after timer: recovered-order");
    }

    [Fact]
    public async Task Should_Coordinate_Distributed_Execution_With_Lock()
    {
        var timerStore = new InMemoryDurableWorkflowTimerStore();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var mockLock = Substitute.For<IDistributedLock>();

        // Lock acquisition succeeds once
        lockProvider.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(mockLock, (IDistributedLock?)null);

        var instanceId = WorkflowInstanceId.New();
        await timerStore.SaveTimerAsync(new DurableWorkflowTimerRecord(instanceId, 1, _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow()));

        int callbackCount = 0;
        var coordinator1 = new DurableWorkflowTimerCoordinator(timerStore, _ => { callbackCount++; return ValueTask.CompletedTask; }, lockProvider);
        var coordinator2 = new DurableWorkflowTimerCoordinator(timerStore, _ => { callbackCount++; return ValueTask.CompletedTask; }, lockProvider);

        var c1Fired = await coordinator1.ProcessDueTimersAsync();
        var c2Fired = await coordinator2.ProcessDueTimersAsync();

        c1Fired.ShouldBe(1);
        c2Fired.ShouldBe(0);
        callbackCount.ShouldBe(1);
    }
}
