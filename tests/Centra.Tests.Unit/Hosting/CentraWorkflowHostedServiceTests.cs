using Centra.Core.Workflows;
using Centra.Workflows;
using Centra.Workflows.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraWorkflowHostedServiceTests
{
    private readonly IWorkflowEngine _engine = Substitute.For<IWorkflowEngine>();
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task Service_Lifecycle_StartsAndStopsGracefully_WhenCancelled()
    {
        var options = new WorkflowOptions
        {
            PollingInterval = TimeSpan.FromSeconds(5)
        };

        var service = new CentraWorkflowHostedService(_engine, options, _timeProvider, NullLogger<CentraWorkflowHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        // Advance fake time to trigger timer delay inside loop
        _timeProvider.Advance(TimeSpan.FromSeconds(5));

        // Stop gracefully
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_DefaultTimeProvider_InstantiatesSuccessfully()
    {
        var service = new CentraWorkflowHostedService(_engine, new WorkflowOptions());
        service.ShouldNotBeNull();
    }

    [Fact]
    public async Task Service_Loop_Handles_Exception_Gracefully()
    {
        var options = new WorkflowOptions { PollingInterval = TimeSpan.FromMilliseconds(10) };
        var service = new CentraWorkflowHostedService(_engine, options, new ThrowingTimeProvider(), NullLogger<CentraWorkflowHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Service_Loop_DoesNotProcessTimers_WhenNoWorkflowsRegistered()
    {
        var timerStore = Substitute.For<IDurableWorkflowTimerStore>();
        var coordinator = new DurableWorkflowTimerCoordinator(
            timerStore,
            _ => ValueTask.CompletedTask,
            timeProvider: _timeProvider);

        var registry = new WorkflowRegistry();
        var options = new WorkflowOptions { PollingInterval = TimeSpan.FromMilliseconds(10) };
        var service = new CentraWorkflowHostedService(
            _engine,
            options,
            coordinator,
            registry,
            _timeProvider,
            NullLogger<CentraWorkflowHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        await service.StopAsync(CancellationToken.None);

        await timerStore.DidNotReceive().GetDueTimersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Service_Loop_ProcessesTimers_WhenWorkflowsRegistered()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timerStore = Substitute.For<IDurableWorkflowTimerStore>();
        timerStore.GetDueTimersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                tcs.TrySetResult(true);
                return Array.Empty<DurableWorkflowTimerRecord>();
            });

        var coordinator = new DurableWorkflowTimerCoordinator(
            timerStore,
            _ => ValueTask.CompletedTask,
            timeProvider: _timeProvider);

        var registry = new WorkflowRegistry();
        registry.RegisterWorkflow(new WorkflowDefinition("TestWf", typeof(object), typeof(object), typeof(object)));

        var options = new WorkflowOptions { PollingInterval = TimeSpan.FromMilliseconds(10) };
        var service = new CentraWorkflowHostedService(
            _engine,
            options,
            coordinator,
            registry,
            _timeProvider,
            NullLogger<CentraWorkflowHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        await timerStore.Received().GetDueTimersAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        private int _thrown;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Exchange(ref _thrown, 1) == 0)
            {
                throw new InvalidOperationException("Timer creation failure");
            }
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
