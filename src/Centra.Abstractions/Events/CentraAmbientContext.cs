namespace Centra.Events;

public static class CentraAmbientContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();
    private static readonly AsyncLocal<string?> _causationId = new();
    private static readonly AsyncLocal<string?> _tenantId = new();

    public static string? CorrelationId
    {
        get => _correlationId.Value;
        set => _correlationId.Value = value;
    }

    public static string? CausationId
    {
        get => _causationId.Value;
        set => _causationId.Value = value;
    }

    public static string? TenantId
    {
        get => _tenantId.Value;
        set => _tenantId.Value = value;
    }

    public static void Clear()
    {
        _correlationId.Value = null;
        _causationId.Value = null;
        _tenantId.Value = null;
    }
}
