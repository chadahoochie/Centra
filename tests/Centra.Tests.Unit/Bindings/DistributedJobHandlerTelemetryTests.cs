using System.Diagnostics.Metrics;
using Centra.Bindings;
using Centra.Diagnostics;
using Centra.Locks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class DistributedJobHandlerTelemetryTests
{
    private readonly IDistributedLockProvider _lockProvider = Substitute.For<IDistributedLockProvider>();
    private readonly IJobHandler _innerHandler = Substitute.For<IJobHandler>();

    [Fact]
    public async Task ExecuteAsync_WhenLockAcquired_ShouldRecordSuccessTrigger()
    {
        var lockObj = Substitute.For<IDistributedLock>();
        _lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>(lockObj));

        using var meterListener = new MeterListener();
        var recorded = false;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "centra.binding.triggers.total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagsList = tags.ToArray();
            var bindingName = tagsList.FirstOrDefault(t => t.Key == "centra.binding.name").Value?.ToString();
            var status = tagsList.FirstOrDefault(t => t.Key == "status").Value?.ToString();

            if (bindingName == "telemetry-cron-job" && status == "success")
            {
                recorded = true;
            }
        });
        meterListener.Start();

        var handler = new DistributedJobHandler(_innerHandler, _lockProvider, "lockstore", NullLogger<DistributedJobHandler>.Instance);
        var scheduledTime = DateTimeOffset.UtcNow;
        var context = new ScheduledJobContext("telemetry-cron-job", scheduledTime, scheduledTime, 1, CancellationToken.None);

        await handler.ExecuteAsync(context);

        recorded.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockSkipped_ShouldRecordSkippedTrigger()
    {
        _lockProvider.TryAcquireLockAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IDistributedLock?>((IDistributedLock?)null));

        using var meterListener = new MeterListener();
        var recorded = false;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "centra.binding.triggers.total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagsList = tags.ToArray();
            var bindingName = tagsList.FirstOrDefault(t => t.Key == "centra.binding.name").Value?.ToString();
            var status = tagsList.FirstOrDefault(t => t.Key == "status").Value?.ToString();

            if (bindingName == "skipped-cron-job" && status == "skipped")
            {
                recorded = true;
            }
        });
        meterListener.Start();

        var handler = new DistributedJobHandler(_innerHandler, _lockProvider, "lockstore", NullLogger<DistributedJobHandler>.Instance);
        var scheduledTime = DateTimeOffset.UtcNow;
        var context = new ScheduledJobContext("skipped-cron-job", scheduledTime, scheduledTime, 1, CancellationToken.None);

        await handler.ExecuteAsync(context);

        recorded.ShouldBeTrue();
    }
}
