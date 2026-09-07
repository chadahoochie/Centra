using Centra.Actors;
using Centra.Core.Actors;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorTimerManagerTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly ActorIdentity _identity;

    public ActorTimerManagerTests()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _identity = new ActorIdentity("TimerActor", "t-101");
    }

    [Fact]
    public void Should_Register_And_Fire_Timer_Ticks()
    {
        var manager = new ActorTimerManager(_identity, _timeProvider);
        var tickCount = 0;

        using var timer = manager.RegisterTimer(
            "heartbeat",
            _ =>
            {
                tickCount++;
                return ValueTask.CompletedTask;
            },
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10));

        // Advance time before due time
        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        tickCount.ShouldBe(0);

        // Advance to due time
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        tickCount.ShouldBe(1);

        // Advance period
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        tickCount.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Stop_Firing_When_Unregistered()
    {
        var manager = new ActorTimerManager(_identity, _timeProvider);
        var tickCount = 0;

        var timer = manager.RegisterTimer(
            "temp-timer",
            _ =>
            {
                tickCount++;
                return ValueTask.CompletedTask;
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        tickCount.ShouldBe(1);

        await manager.UnregisterTimerAsync("temp-timer");

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        tickCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Dispose_All_Timers_On_Manager_Disposal()
    {
        var manager = new ActorTimerManager(_identity, _timeProvider);
        var tickCount = 0;

        _ = manager.RegisterTimer(
            "timer-1",
            _ =>
            {
                tickCount++;
                return ValueTask.CompletedTask;
            },
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));

        await manager.DisposeAsync();

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        tickCount.ShouldBe(0);
    }
}
