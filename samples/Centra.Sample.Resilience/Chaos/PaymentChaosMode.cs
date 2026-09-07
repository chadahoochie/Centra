namespace Centra.Sample.Resilience.Chaos;

public enum PaymentChaosMode
{
    Normal,
    TransientBlip,
    Outage,
    LatencySpike
}
