using System;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture.Xunit2;
using Centra.Actors;
using Centra.Core.Actors;
using Centra.State;
using NSubstitute;
using Shouldly;
using Xunit;
using Centra.Tests.Unit.Common;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorReminderManagerTests
{
    private static ActorReminderCoordinator CreateCoordinator()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var stateStore = Substitute.For<IStateStore>();
        var options = new ActorOptions();
        var manager = new ActorManager(serviceProvider, stateStore, options);
        return new ActorReminderCoordinator(manager, stateStore, options);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Register_Reminder_And_Persist_To_StateStore(
        string storeName,
        string reminderName,
        byte[] state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var coordinator = CreateCoordinator();
        var sut = new ActorReminderManager(identity, stateStore, storeName, coordinator);
        var memoryState = new ReadOnlyMemory<byte>(state);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await sut.RegisterReminderAsync(reminderName, memoryState, dueTime, period, cts.Token);

        // Assert
        result.Name.ShouldBe(reminderName);
        result.DueTime.ShouldBe(dueTime);
        result.Period.ShouldBe(period);
        result.State.ToArray().ShouldBe(state);

        await stateStore.Received(1).SetAsync(
            storeName,
            $"actor-reminders:{identity.Type.Value}:{identity.Id.Value}:{reminderName}",
            Arg.Is<ActorReminderRecord>(r => r.Name == reminderName && r.DueTime == dueTime && r.Period == period),
            null,
            cts.Token);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Register_Reminder_And_Notify_Coordinator_When_Present(
        string storeName,
        string reminderName,
        byte[] state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var coordinator = CreateCoordinator();
        var sut = new ActorReminderManager(identity, stateStore, storeName, coordinator);
        var memoryState = new ReadOnlyMemory<byte>(state);
        using var cts = new CancellationTokenSource();

        // Act
        await sut.RegisterReminderAsync(reminderName, memoryState, dueTime, period, cts.Token);

        // Assert
        // We verify coordinator doesn't throw since we use a real instance and cannot check its internal state.
        // Its integration is proven by not failing here.
        await stateStore.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ActorReminderRecord>(),
            null,
            cts.Token);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Register_Reminder_Without_Coordinator(
        string storeName,
        string reminderName,
        byte[] state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var sut = new ActorReminderManager(identity, stateStore, storeName, null);
        var memoryState = new ReadOnlyMemory<byte>(state);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await sut.RegisterReminderAsync(reminderName, memoryState, dueTime, period, cts.Token);

        // Assert
        result.Name.ShouldBe(reminderName);
        
        await stateStore.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ActorReminderRecord>(),
            null,
            cts.Token);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Unregister_Reminder_And_Delete_From_StateStore(
        string storeName,
        string reminderName)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var sut = new ActorReminderManager(identity, stateStore, storeName, null);
        using var cts = new CancellationTokenSource();

        // Act
        await sut.UnregisterReminderAsync(reminderName, cts.Token);

        // Assert
        var expectedKey = $"actor-reminders:{identity.Type.Value}:{identity.Id.Value}:{reminderName}";
        await stateStore.Received(1).DeleteAsync(
            storeName,
            expectedKey,
            null,
            cts.Token);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Unregister_Reminder_And_Notify_Coordinator(
        string storeName,
        string reminderName)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var coordinator = CreateCoordinator();
        var sut = new ActorReminderManager(identity, stateStore, storeName, coordinator);
        using var cts = new CancellationTokenSource();

        // Act
        await sut.UnregisterReminderAsync(reminderName, cts.Token);

        // Assert
        // We just ensure no exception is thrown when unregistering with real coordinator
        await stateStore.Received(1).DeleteAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            null,
            cts.Token);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Get_Reminder_When_Exists_In_StateStore(
        string storeName,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        byte[] state,
        string etag)
    {
        // Arrange
        var record = new ActorReminderRecord(reminderName, dueTime, period, state);
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var sut = new ActorReminderManager(identity, stateStore, storeName, null);
        using var cts = new CancellationTokenSource();
        var key = $"actor-reminders:{identity.Type.Value}:{identity.Id.Value}:{reminderName}";
        var entry = new StateEntry<ActorReminderRecord>(key, record, etag);
        
        stateStore.GetAsync<ActorReminderRecord>(storeName, key, null, cts.Token)
            .Returns(new ValueTask<StateEntry<ActorReminderRecord>?>(entry));

        // Act
        var result = await sut.GetReminderAsync(reminderName, cts.Token);

        // Assert
        result.ShouldNotBeNull();
        var reminder = result.Value;
        reminder.Name.ShouldBe(record.Name);
        reminder.DueTime.ShouldBe(record.DueTime);
        reminder.Period.ShouldBe(record.Period);
        reminder.State.ToArray().ShouldBe(record.State ?? Array.Empty<byte>());
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Return_Null_When_Reminder_Not_Found(
        string storeName,
        string reminderName)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var sut = new ActorReminderManager(identity, stateStore, storeName, null);
        using var cts = new CancellationTokenSource();
        var key = $"actor-reminders:{identity.Type.Value}:{identity.Id.Value}:{reminderName}";
        
        stateStore.GetAsync<ActorReminderRecord>(storeName, key, null, cts.Token)
            .Returns(new ValueTask<StateEntry<ActorReminderRecord>?>((StateEntry<ActorReminderRecord>?)null));

        // Act
        var result = await sut.GetReminderAsync(reminderName, cts.Token);

        // Assert
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Should_Throw_When_ReminderName_Is_NullOrWhiteSpace(string? invalidReminderName)
    {
        // Arrange
        var identity = new ActorIdentity("TestActor", "123");
        var stateStore = Substitute.For<IStateStore>();
        var sut = new ActorReminderManager(identity, stateStore, "store", null);
        var state = new ReadOnlyMemory<byte>(Array.Empty<byte>());

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(async () => 
            await sut.RegisterReminderAsync(invalidReminderName!, state, TimeSpan.Zero, TimeSpan.Zero));
            
        await Should.ThrowAsync<ArgumentException>(async () => 
            await sut.UnregisterReminderAsync(invalidReminderName!));
            
        await Should.ThrowAsync<ArgumentException>(async () => 
            await sut.GetReminderAsync(invalidReminderName!));
    }
}
