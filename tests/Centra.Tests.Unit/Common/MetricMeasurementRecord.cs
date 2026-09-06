namespace Centra.Tests.Unit.Common;

public readonly record struct MetricMeasurementRecord(
    string InstrumentName,
    object Value,
    KeyValuePair<string, object?>[] Tags);
