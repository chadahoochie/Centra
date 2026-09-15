namespace Centra.PubSub.Tenancy;

[Flags]
public enum TenantOffloadReason
{
    None = 0,
    HighTrafficShare = 1,
    DisproportionateOperationDuration = 2,
    Manual = 4
}
