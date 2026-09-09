using Centra.Actors;
using Centra.Core.Actors;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorStateComponentsTests
{
    [Fact]
    public void ActorStateKeyFormatter_FormatsKeyCorrectly()
    {
        var formatter = ActorStateKeyFormatter.Instance;
        var identity = new ActorIdentity(new ActorType("OrderActor"), new ActorId("12345"));

        var key = formatter.FormatStateKey(identity, "profile");

        Assert.Equal("actors:OrderActor:12345:profile", key);
    }

    [Fact]
    public void ActorReminderKeyFormatter_FormatsStorageScheduleAndLockKeys()
    {
        var formatter = ActorReminderKeyFormatter.Instance;
        var identity = new ActorIdentity(new ActorType("OrderActor"), new ActorId("12345"));

        var storageKey = formatter.FormatStorageKey(identity, "cleanup");
        var scheduleKey = formatter.FormatScheduleKey(identity, "cleanup");
        var lockKey = formatter.FormatLockKey(identity, "cleanup");

        Assert.Equal("actor-reminders:OrderActor:12345:cleanup", storageKey);
        Assert.Equal("OrderActor:12345:cleanup", scheduleKey);
        Assert.Equal("actor-reminder:OrderActor/12345:cleanup:lock", lockKey);
    }

    [Fact]
    public void ActorReminderScheduleCalculator_AdvancesPeriodicAndEvictsNonPeriodic()
    {
        var calculator = ActorReminderScheduleCalculator.Instance;
        var timeProvider = TimeProvider.System;
        var identity = new ActorIdentity(new ActorType("OrderActor"), new ActorId("12345"));

        var periodicSchedule = new ActorReminderSchedule(
            identity,
            "periodic",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(1),
            null,
            timeProvider.GetUtcNow());

        var oneShotSchedule = new ActorReminderSchedule(
            identity,
            "oneshot",
            TimeSpan.FromSeconds(10),
            TimeSpan.Zero,
            null,
            timeProvider.GetUtcNow());

        var periodicAdvanced = calculator.TryAdvanceSchedule(periodicSchedule, timeProvider);
        var oneShotAdvanced = calculator.TryAdvanceSchedule(oneShotSchedule, timeProvider);

        Assert.True(periodicAdvanced);
        Assert.True(periodicSchedule.NextDueUtc > timeProvider.GetUtcNow());
        Assert.False(oneShotAdvanced);
    }
}
