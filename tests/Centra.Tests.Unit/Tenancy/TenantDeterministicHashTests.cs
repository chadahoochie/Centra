using Centra.PubSub.Tenancy;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class TenantDeterministicHashTests
{
    [Theory]
    [InlineData("tenant-alpha", 4)]
    [InlineData("tenant-beta", 4)]
    [InlineData("tenant-gamma", 8)]
    [InlineData("tenant-delta", 16)]
    public void GetShardId_ReturnsDeterministicShardAcrossCalls(string tenantId, int shardCount)
    {
        var first = TenantDeterministicHash.GetShardId(tenantId, shardCount);
        var second = TenantDeterministicHash.GetShardId(tenantId, shardCount);

        Assert.Equal(first, second);
        Assert.InRange(first, 0, shardCount - 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetShardId_WithNullOrEmptyTenant_ReturnsZero(string? tenantId)
    {
        var shardId = TenantDeterministicHash.GetShardId(tenantId!, 4);
        Assert.Equal(0, shardId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-5)]
    public void GetShardId_WithShardCountLessThanOrEqualToOne_ReturnsZero(int shardCount)
    {
        var shardId = TenantDeterministicHash.GetShardId("tenant-123", shardCount);
        Assert.Equal(0, shardId);
    }

    [Fact]
    public void GetShardId_DistributesAcrossMultipleShards()
    {
        var shardsSeen = new HashSet<int>();
        for (var i = 0; i < 50; i++)
        {
            var shard = TenantDeterministicHash.GetShardId($"tenant-{i}", 4);
            shardsSeen.Add(shard);
        }

        Assert.True(shardsSeen.Count > 1, "Hash should distribute tenants across multiple shards");
    }
}
