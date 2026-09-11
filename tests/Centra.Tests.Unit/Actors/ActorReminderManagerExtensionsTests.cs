using Centra.Actors;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorReminderManagerExtensionsTests
{
    [Fact]
    public async Task RegisterReminderAsync_Without_State_Should_Forward_Empty_Memory()
    {
        var manager = Substitute.For<IActorReminderManager>();
        var reminder = new ActorReminder("test-reminder", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
        
        manager.RegisterReminderAsync(
            "test-reminder",
            Arg.Is<ReadOnlyMemory<byte>>(m => m.IsEmpty),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(1),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(reminder));

        var result = await manager.RegisterReminderAsync("test-reminder", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));

        result.ShouldBe(reminder);
        await manager.Received(1).RegisterReminderAsync(
            "test-reminder",
            Arg.Is<ReadOnlyMemory<byte>>(m => m.IsEmpty),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RegisterReminderAsync_Should_Throw_When_Manager_Is_Null()
    {
        IActorReminderManager manager = null!;
        Should.Throw<ArgumentNullException>(() =>
            manager.RegisterReminderAsync("rem", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
    }
}
