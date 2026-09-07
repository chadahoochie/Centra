using Centra.Actors;
using Centra.Core.Actors;
using Centra.Locks;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorReminderCoordinatorTests
{
    private readonly IStateStore _stateStore;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ActorOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ActorManager _actorManager;

    public ActorReminderCoordinatorTests()
    {
        _stateStore = Substitute.For<IStateStore>();
        _lockProvider = Substitute.For<IDistributedLockProvider>();
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _options = new ActorOptions { DefaultStateStore = "statestore" };

        var services = new ServiceCollection();
        services.AddTransient<RemindableTestActor>();
        _serviceProvider = services.BuildServiceProvider();

        var mockLock = Substitute.For<IDistributedLock>();
        _lockProvider.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(mockLock);

        _actorManager = new ActorManager(_serviceProvider, _stateStore, _options, _timeProvider);
    }

    [Fact]
    public async Task Should_Fire_Reminder_When_Due_And_Invoke_ReceiveReminderAsync()
    {
        // Arrange
        var coordinator = new ActorReminderCoordinator(_actorManager, _stateStore, _options, _lockProvider, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<RemindableTestActor>(), "rem-101");
        var state = new byte[] { 10, 20, 30 };

        coordinator.RegisterReminder(identity, "daily-backup", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), state);

        // Advance time before due time
        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        await coordinator.TickAsync();

        // Advance to due time
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await coordinator.TickAsync();

        // Assert: actor was activated and reminder received
        _actorManager.ActiveCount.ShouldBe(1);

        var remindersReceived = await _actorManager.DispatchAsync(identity, actor =>
        {
            var remindable = (RemindableTestActor)actor;
            return ValueTask.FromResult(remindable.ReceivedReminders.Count);
        });

        remindersReceived.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Reactivate_Passivated_Actor_To_Execute_Reminder()
    {
        // Arrange
        var coordinator = new ActorReminderCoordinator(_actorManager, _stateStore, _options, _lockProvider, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<RemindableTestActor>(), "rem-passivate");

        coordinator.RegisterReminder(identity, "wake-up", TimeSpan.FromSeconds(10), TimeSpan.Zero, null);

        // Actor is initially inactive
        _actorManager.ActiveCount.ShouldBe(0);

        // Advance time to trigger reminder
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        await coordinator.TickAsync();

        // Assert: actor was automatically activated to handle reminder!
        _actorManager.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Reschedule_Recurring_Reminder_After_Execution()
    {
        // Arrange
        var coordinator = new ActorReminderCoordinator(_actorManager, _stateStore, _options, _lockProvider, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<RemindableTestActor>(), "rem-recur");

        coordinator.RegisterReminder(identity, "recurring-tick", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), null);

        // First tick
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        await coordinator.TickAsync();

        // Second tick (after 10s period)
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        await coordinator.TickAsync();

        var count = await _actorManager.DispatchAsync(identity, actor =>
        {
            var remindable = (RemindableTestActor)actor;
            return ValueTask.FromResult(remindable.ReceivedReminders.Count);
        });

        count.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Unregister_Reminder_Successfully()
    {
        // Arrange
        var coordinator = new ActorReminderCoordinator(_actorManager, _stateStore, _options, _lockProvider, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<RemindableTestActor>(), "rem-unreg");

        coordinator.RegisterReminder(identity, "cancel-me", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), null);
        coordinator.UnregisterReminder(identity, "cancel-me");

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        await coordinator.TickAsync();

        _actorManager.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Skip_Execution_When_Distributed_Lock_Cannot_Be_Acquired()
    {
        // Arrange: mock lock acquisition returning null (lock held by another node)
        _lockProvider.TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLock?)null);

        var coordinator = new ActorReminderCoordinator(_actorManager, _stateStore, _options, _lockProvider, _timeProvider);
        var identity = new ActorIdentity(ActorType.FromType<RemindableTestActor>(), "rem-locked");

        coordinator.RegisterReminder(identity, "locked-tick", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), null);

        // Act
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        var executedCount = await coordinator.TickAsync();

        // Assert
        executedCount.ShouldBe(0);
        _actorManager.ActiveCount.ShouldBe(0);
    }

    public sealed class RemindableTestActor : Actor, IRemindable
    {
        public List<(string Name, ReadOnlyMemory<byte> State)> ReceivedReminders { get; } = new();

        public ValueTask ReceiveReminderAsync(
            string reminderName,
            ReadOnlyMemory<byte> state,
            TimeSpan dueTime,
            TimeSpan period,
            CancellationToken cancellationToken = default)
        {
            ReceivedReminders.Add((reminderName, state));
            return ValueTask.CompletedTask;
        }
    }
}
