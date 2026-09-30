namespace Centra.Providers.Flotilla.Client;

/// <summary>
/// Result of submitting a client proposal to the Flotilla Raft consensus cluster.
/// </summary>
public sealed class FlotillaProposalResult
{
    public bool IsSuccess { get; }
    public ulong LogIndex { get; }
    public string? ErrorMessage { get; }

    private FlotillaProposalResult(bool isSuccess, ulong logIndex, string? errorMessage)
    {
        IsSuccess = isSuccess;
        LogIndex = logIndex;
        ErrorMessage = errorMessage;
    }

    public static FlotillaProposalResult Success(ulong logIndex) =>
        new(true, logIndex, null);

    public static FlotillaProposalResult Failure(string errorMessage) =>
        new(false, 0, errorMessage);
}
