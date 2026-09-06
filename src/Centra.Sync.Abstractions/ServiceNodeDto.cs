namespace Centra.Sync;

public sealed record ServiceNodeDto(
    string AppId,
    string InstanceId,
    string Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    IReadOnlyDictionary<string, string>? Metadata);
