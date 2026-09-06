namespace Centra.Sample.MultiInstance.Domain;

public sealed record IncrementCounterResponse(
    bool Success,
    long Value,
    string? ETag,
    int RetryAttempts,
    string Message);
