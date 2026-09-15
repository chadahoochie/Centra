namespace Centra.PubSub.Tenancy;

public static class TenantDeterministicHash
{
    public static int GetShardId(string tenantId, int shardCount)
    {
        if (shardCount <= 1 || string.IsNullOrEmpty(tenantId))
        {
            return 0;
        }

        // 32-bit FNV-1a hash - deterministic across runtimes, platforms, and process restarts
        uint hash = 2166136261;
        for (var i = 0; i < tenantId.Length; i++)
        {
            hash = (hash ^ tenantId[i]) * 16777619;
        }

        return (int)(hash % (uint)shardCount);
    }
}
