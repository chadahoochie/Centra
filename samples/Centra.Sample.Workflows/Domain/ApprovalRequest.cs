namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Domain model requesting manager approval.
/// </summary>
public sealed record ApprovalRequest(
    string RequestId,
    string Requester,
    decimal Amount,
    string Purpose);
