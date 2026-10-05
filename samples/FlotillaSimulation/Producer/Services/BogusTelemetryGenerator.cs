using Bogus;
using Centra.Sample.FlotillaSimulation.Contracts.Models;

namespace Centra.Sample.FlotillaSimulation.Producer.Services;

public sealed class BogusTelemetryGenerator
{
    private readonly Faker<TelemetryEvent> _faker;

    public BogusTelemetryGenerator()
    {
        var deviceIds = new[] { "TURBINE-01", "TURBINE-02", "COOLER-A", "PUMP-404", "REACTOR-CORE-1" };
        var locations = new[] { "Facility-Austin-TX", "DataCenter-Ashburn-VA", "Refinery-Houston-TX", "Plant-Dublin-OH" };
        var statuses = new[] { "Nominal", "Nominal", "Nominal", "Warning", "Critical" };

        _faker = new Faker<TelemetryEvent>()
            .CustomInstantiator(f => new TelemetryEvent(
                DeviceId: f.PickRandom(deviceIds),
                Location: f.PickRandom(locations),
                Temperature: Math.Round(f.Random.Double(45.0, 115.0), 2),
                Pressure: Math.Round(f.Random.Double(25.0, 95.0), 2),
                Vibration: Math.Round(f.Random.Double(0.5, 9.8), 2),
                Status: f.PickRandom(statuses),
                Timestamp: DateTimeOffset.UtcNow));
    }

    public TelemetryEvent Generate()
    {
        return _faker.Generate();
    }
}
