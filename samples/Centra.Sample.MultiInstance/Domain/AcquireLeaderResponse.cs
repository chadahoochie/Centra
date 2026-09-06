namespace Centra.Sample.MultiInstance.Domain;

public sealed record AcquireLeaderResponse(
    bool Acquired,
    string InstanceId,
    string Message,
    TimeSpan? LeaseDuration);
