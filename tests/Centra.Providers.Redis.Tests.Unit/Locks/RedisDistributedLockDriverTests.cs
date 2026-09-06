using Centra.Providers.Redis.Locks;
using Centra.Providers.Redis.Options;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.Locks;

public sealed class RedisDistributedLockDriverTests
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly RedisProviderOptions _options;
    private readonly RedisDistributedLockDriver _sut;

    public RedisDistributedLockDriverTests()
    {
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _database = Substitute.For<IDatabase>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);

        _options = new RedisProviderOptions { KeyPrefix = "centra:" };
        _sut = new RedisDistributedLockDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(_options));
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Lock_When_Key_Set_Succeeds()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(true);

        var @lock = await _sut.TryAcquireLockAsync("lockstore", "order-1", TimeSpan.FromSeconds(30));

        @lock.ShouldNotBeNull();
        @lock.ResourceId.ShouldBe("order-1");
        @lock.LockId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Null_When_Key_Already_Exists()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>())
            .Returns(false);

        var @lock = await _sut.TryAcquireLockAsync("lockstore", "order-1", TimeSpan.FromSeconds(30));

        @lock.ShouldBeNull();
    }

    [Fact]
    public async Task Lock_DisposeAsync_Should_Execute_Release_Script()
    {
        var redisLock = new RedisDistributedLock(_database, "centra:lock:lockstore:order-1", "order-1", "lock-guid");

        await redisLock.DisposeAsync();

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == "centra:lock:lockstore:order-1"),
            Arg.Is<RedisValue[]>(vals => vals.Length == 1 && vals[0] == "lock-guid"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Lock_RenewAsync_Should_Execute_Renew_Script()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1L));

        var redisLock = new RedisDistributedLock(_database, "centra:lock:lockstore:order-1", "order-1", "lock-guid");

        var success = await redisLock.RenewAsync(TimeSpan.FromSeconds(60));

        success.ShouldBeTrue();
    }
}
