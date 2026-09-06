using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Centra.Tests.Unit.Common;

public sealed class TestMeterListener : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<MetricMeasurementRecord> _measurements = new();

    public IReadOnlyCollection<MetricMeasurementRecord> Measurements => _measurements;

    public TestMeterListener(string meterName = "Centra")
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _measurements.Add(new MetricMeasurementRecord(instrument.Name, measurement, tags.ToArray()));
        });

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            _measurements.Add(new MetricMeasurementRecord(instrument.Name, measurement, tags.ToArray()));
        });

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
