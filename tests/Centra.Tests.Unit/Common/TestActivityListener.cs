using System.Collections.Concurrent;
using System.Diagnostics;

namespace Centra.Tests.Unit.Common;

public sealed class TestActivityListener : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentBag<Activity> _stoppedActivities = new();

    public IReadOnlyCollection<Activity> StoppedActivities => _stoppedActivities;

    public TestActivityListener(string sourceName = "Centra")
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _stoppedActivities.Add(activity)
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
