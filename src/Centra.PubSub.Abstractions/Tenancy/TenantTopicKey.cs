namespace Centra.PubSub.Tenancy;

public readonly record struct TenantTopicKey(string Topic, string TenantId) : IEquatable<TenantTopicKey>
{
    public const string OverallTenantId = "__overall__";

    public bool Equals(TenantTopicKey other) =>
        string.Equals(Topic, other.Topic, StringComparison.Ordinal) &&
        string.Equals(TenantId, other.TenantId, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Topic ?? string.Empty),
            StringComparer.Ordinal.GetHashCode(TenantId ?? string.Empty));
}
