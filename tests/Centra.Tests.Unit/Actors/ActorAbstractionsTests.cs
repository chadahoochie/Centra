using Centra.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorAbstractionsTests
{
    [Fact]
    public void ActorId_Should_Validate_And_Support_Implicit_Conversions()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new ActorId(""));
        Should.Throw<ArgumentException>(() => new ActorId("   "));

        var id = new ActorId("device-101");
        id.Value.ShouldBe("device-101");
        id.IsEmpty.ShouldBeFalse();
        id.ToString().ShouldBe("device-101");

        string raw = id;
        raw.ShouldBe("device-101");

        ActorId fromString = "device-202";
        fromString.Value.ShouldBe("device-202");

        var autoId = ActorId.Create();
        autoId.IsEmpty.ShouldBeFalse();
        autoId.Value.Length.ShouldBe(32);
    }

    [Fact]
    public void ActorType_Should_Validate_And_Support_Type_Extraction()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new ActorType(""));
        Should.Throw<ArgumentException>(() => new ActorType("   "));

        var type = new ActorType("DeviceActor");
        type.Value.ShouldBe("DeviceActor");
        type.IsEmpty.ShouldBeFalse();
        type.ToString().ShouldBe("DeviceActor");

        string raw = type;
        raw.ShouldBe("DeviceActor");

        ActorType fromString = "OrderActor";
        fromString.Value.ShouldBe("OrderActor");

        var fromGeneric = ActorType.FromType<TestSampleActor>();
        fromGeneric.Value.ShouldBe("TestSampleActor");

        var fromType = ActorType.FromType(typeof(TestSampleActor));
        fromType.Value.ShouldBe("TestSampleActor");
    }

    [Fact]
    public void ActorIdentity_Should_Encapsulate_Type_And_Id_And_Format_String()
    {
        var type = new ActorType("DeviceActor");
        var id = new ActorId("device-101");

        var identity = new ActorIdentity(type, id);

        identity.Type.ShouldBe(type);
        identity.Id.ShouldBe(id);
        identity.ToString().ShouldBe("DeviceActor/device-101");

        // Value equality
        var identity2 = new ActorIdentity(new ActorType("DeviceActor"), new ActorId("device-101"));
        identity.ShouldBe(identity2);
        (identity == identity2).ShouldBeTrue();

        var identity3 = new ActorIdentity(new ActorType("DeviceActor"), new ActorId("device-999"));
        (identity == identity3).ShouldBeFalse();

        Should.Throw<ArgumentException>(() => new ActorIdentity(default, id));
        Should.Throw<ArgumentException>(() => new ActorIdentity(type, default));
    }

    [Fact]
    public void ActorOptions_Should_Have_Sensible_Defaults()
    {
        var options = new ActorOptions();

        options.ActorIdleTimeout.ShouldBe(TimeSpan.FromMinutes(15));
        options.ActorScanInterval.ShouldBe(TimeSpan.FromSeconds(30));
        options.DrainTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.ReminderInterval.ShouldBe(TimeSpan.FromSeconds(10));
        options.DefaultStateStore.ShouldBe("statestore");
    }

    [Fact]
    public void ActorReminder_Should_Hold_Schedule_And_State()
    {
        var state = new byte[] { 1, 2, 3, 4 };
        var reminder = new ActorReminder("backup-reminder", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), state);

        reminder.Name.ShouldBe("backup-reminder");
        reminder.DueTime.ShouldBe(TimeSpan.FromSeconds(5));
        reminder.Period.ShouldBe(TimeSpan.FromMinutes(1));
        reminder.State.ToArray().ShouldBe(state);
    }

    [Fact]
    public async Task ActorTimer_Should_Invoke_Dispose_Handler_Once()
    {
        var disposeCount = 0;
        var timer = new ActorTimer("test-timer", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), () =>
        {
            disposeCount++;
            return ValueTask.CompletedTask;
        });

        timer.Name.ShouldBe("test-timer");
        timer.DueTime.ShouldBe(TimeSpan.FromSeconds(1));
        timer.Period.ShouldBe(TimeSpan.FromSeconds(2));

        await timer.DisposeAsync();
        await timer.DisposeAsync();

        disposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Actor_Base_Class_Lifecycle_Hooks_Should_Complete_Successfully()
    {
        var actor = new TestSampleActor();
        actor.Identity = new ActorIdentity("TestSampleActor", "inst-1");

        actor.Id.Value.ShouldBe("inst-1");
        actor.Type.Value.ShouldBe("TestSampleActor");

        await actor.OnActivateAsync();
        await actor.OnDeactivateAsync();
    }

    private sealed class TestSampleActor : Actor
    {
    }
}
