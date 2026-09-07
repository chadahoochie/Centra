namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Domain model representing manager's decision on approval.
/// </summary>
public sealed record ApprovalResponse(
    string ApproverId,
    bool Approved,
    string? Comments);
