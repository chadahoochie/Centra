using System.Text;
using Centra.Bindings;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class BindingAbstractionsTests
{
    [Fact]
    public void BindingData_ShouldInitializeCorrectly()
    {
        var dataBytes = Encoding.UTF8.GetBytes("hello bindings");
        var metadata = new Dictionary<string, string> { ["source"] = "webhook" };
        var bindingData = new BindingData(dataBytes, metadata, "text/plain");

        bindingData.Data.ToArray().ShouldBe(dataBytes);
        bindingData.Metadata.ShouldNotBeNull();
        bindingData.Metadata["source"].ShouldBe("webhook");
        bindingData.ContentType.ShouldBe("text/plain");
    }

    [Fact]
    public void BindingData_DefaultValues_ShouldBeNull()
    {
        var bindingData = new BindingData(ReadOnlyMemory<byte>.Empty);

        bindingData.Data.IsEmpty.ShouldBeTrue();
        bindingData.Metadata.ShouldBeNull();
        bindingData.ContentType.ShouldBeNull();
    }

    [Fact]
    public void BindingRequest_ShouldInitializeCorrectly()
    {
        var dataBytes = Encoding.UTF8.GetBytes("test payload");
        var metadata = new Dictionary<string, string> { ["header1"] = "val1" };
        var request = new BindingRequest(dataBytes, metadata, "create");

        request.Data.ToArray().ShouldBe(dataBytes);
        request.Metadata.ShouldNotBeNull();
        request.Metadata["header1"].ShouldBe("val1");
        request.Operation.ShouldBe("create");
    }

    [Fact]
    public void BindingResponse_ShouldInitializeCorrectly()
    {
        var dataBytes = Encoding.UTF8.GetBytes("response payload");
        var metadata = new Dictionary<string, string> { ["status"] = "ok" };
        var response = new BindingResponse(dataBytes, metadata);

        response.Data.ToArray().ShouldBe(dataBytes);
        response.Metadata.ShouldNotBeNull();
        response.Metadata["status"].ShouldBe("ok");
    }

    [Fact]
    public void BindingAttribute_ShouldHaveCorrectProperties()
    {
        var defaultAttr = new BindingAttribute("my-binding");
        defaultAttr.BindingName.ShouldBe("my-binding");
        defaultAttr.Direction.ShouldBe(BindingDirection.Input);

        var customAttr = new BindingAttribute("out-binding", BindingDirection.Output);
        customAttr.BindingName.ShouldBe("out-binding");
        customAttr.Direction.ShouldBe(BindingDirection.Output);
    }

    [Fact]
    public void CronBindingAttribute_ShouldHaveCorrectProperties()
    {
        var defaultAttr = new CronBindingAttribute("*/5 * * * *");
        defaultAttr.CronExpression.ShouldBe("*/5 * * * *");
        defaultAttr.MissedRunBehavior.ShouldBe(CronMissedRunBehavior.Skip);

        var customAttr = new CronBindingAttribute("0 0 * * *")
        {
            MissedRunBehavior = CronMissedRunBehavior.CatchUp
        };
        customAttr.CronExpression.ShouldBe("0 0 * * *");
        customAttr.MissedRunBehavior.ShouldBe(CronMissedRunBehavior.CatchUp);
    }

    [Fact]
    public void ScheduledJobContext_ShouldHoldExecutionMetadata()
    {
        var scheduled = DateTimeOffset.UtcNow;
        var actual = scheduled.AddMilliseconds(5);
        using var cts = new CancellationTokenSource();

        var context = new ScheduledJobContext("daily-cleanup", scheduled, actual, 42, cts.Token);

        context.JobName.ShouldBe("daily-cleanup");
        context.ScheduledTime.ShouldBe(scheduled);
        context.ActualTime.ShouldBe(actual);
        context.Iteration.ShouldBe(42);
        context.CancellationToken.ShouldBe(cts.Token);
    }

    [Fact]
    public void CronScheduleOptions_ShouldHaveSensibleDefaults()
    {
        var options = new CronScheduleOptions();

        options.TimeZone.ShouldBeNull();
        options.MissedRunBehavior.ShouldBe(CronMissedRunBehavior.Skip);
        options.UseDistributedCoordination.ShouldBeTrue();
        options.LockTimeout.ShouldBeNull();

        var customTz = TimeZoneInfo.Utc;
        var custom = new CronScheduleOptions(
            TimeZone: customTz,
            MissedRunBehavior: CronMissedRunBehavior.RunOnce,
            UseDistributedCoordination: false,
            LockTimeout: TimeSpan.FromSeconds(10));

        custom.TimeZone.ShouldBe(customTz);
        custom.MissedRunBehavior.ShouldBe(CronMissedRunBehavior.RunOnce);
        custom.UseDistributedCoordination.ShouldBeFalse();
        custom.LockTimeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void BindingDefinition_ShouldHoldConfiguration()
    {
        var metadata = new Dictionary<string, string> { ["url"] = "https://example.com/webhook" };
        var def = new BindingDefinition("orders-out", "http", BindingDirection.Output, metadata);

        def.Name.ShouldBe("orders-out");
        def.Type.ShouldBe("http");
        def.Direction.ShouldBe(BindingDirection.Output);
        def.Metadata.ShouldNotBeNull();
        def.Metadata["url"].ShouldBe("https://example.com/webhook");
    }

    [Fact]
    public void Enums_ShouldContainExpectedValues()
    {
        Enum.GetValues<BindingDirection>().Length.ShouldBe(3);
        Enum.GetValues<CronMissedRunBehavior>().Length.ShouldBe(3);
    }
}
