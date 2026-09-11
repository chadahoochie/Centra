using System.Diagnostics;
using System.Diagnostics.Metrics;
using Centra.ControlPlane.Diagnostics;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Diagnostics;

public sealed class ControlPlaneDiagnosticsTests
{
    [Fact]
    public void StartCatalogActivity_WithListener_SetsTagsCorrectly()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ControlPlaneDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = ControlPlaneDiagnostics.StartCatalogActivity("register", "test-comp");
        activity.ShouldNotBeNull();
        activity.GetTagItem("centra.component.name").ShouldBe("test-comp");
        activity.GetTagItem("centra.operation").ShouldBe("register");
    }

    [Fact]
    public void StartSyncActivity_WithListener_SetsTagsCorrectly()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ControlPlaneDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activityWithComp = ControlPlaneDiagnostics.StartSyncActivity("Upserted", "comp1");
        activityWithComp.ShouldNotBeNull();
        activityWithComp.GetTagItem("centra.sync.type").ShouldBe("Upserted");
        activityWithComp.GetTagItem("centra.component.name").ShouldBe("comp1");

        using var activityNoComp = ControlPlaneDiagnostics.StartSyncActivity("Deleted", null);
        activityNoComp.ShouldNotBeNull();
        activityNoComp.GetTagItem("centra.sync.type").ShouldBe("Deleted");
    }

    [Fact]
    public void ControlPlaneMeters_RecordMetricsWithoutError()
    {
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ControlPlaneMeters.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        var recorded = 0;
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            recorded++;
        });
        meterListener.Start();

        ControlPlaneMeters.RecordComponentRegistered("store1", "state");
        ControlPlaneMeters.RecordSyncEventDispatched("Upserted");
        ControlPlaneMeters.RecordHeartbeatReceived("app1", "Healthy");

        recorded.ShouldBeGreaterThanOrEqualTo(3);
    }
}
