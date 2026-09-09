using Centra.Core.Workflows;
using Centra.Serialization;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowComponentsTests
{
    private readonly ICentraSerializer _serializer = new JsonCentraSerializer();

    [Fact]
    public void WorkflowInputConverter_HandlesNullMatchingTypesAndSerialization()
    {
        var converter = WorkflowInputConverter.Instance;

        // Null value
        var nullInt = converter.ConvertInput(null, typeof(int), _serializer);
        Assert.Equal(0, nullInt);

        var nullString = converter.ConvertInput(null, typeof(string), _serializer);
        Assert.Null(nullString);

        // Direct instance
        var str = converter.ConvertInput("hello", typeof(string), _serializer);
        Assert.Equal("hello", str);

        // Bytes
        var serialized = _serializer.Serialize(42);
        var deserialized = converter.ConvertInput(serialized, typeof(int), _serializer);
        Assert.Equal(42, deserialized);
    }

    [Fact]
    public void WorkflowTimerScheduler_SchedulesAndCancelsTimers()
    {
        using var scheduler = new WorkflowTimerScheduler();
        var id = Centra.Workflows.WorkflowInstanceId.New();

        scheduler.ScheduleTimer(id, TimeSpan.FromHours(1), _ => ValueTask.CompletedTask);

        var canceled = scheduler.CancelTimer(id);
        Assert.True(canceled);

        var notFound = scheduler.CancelTimer(id);
        Assert.False(notFound);
    }
}
