using System.Text;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.State;
using Centra.State;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.State;

public sealed class RedisStateStoreDriverTests
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly RedisProviderOptions _options;
    private readonly RedisStateStoreDriver _sut;

    public RedisStateStoreDriverTests()
    {
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _database = Substitute.For<IDatabase>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);

        _options = new RedisProviderOptions { KeyPrefix = "centra:" };
        _sut = new RedisStateStoreDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(_options));
    }

    [Fact]
    public async Task GetAsync_Should_Return_Null_When_Key_Not_Found()
    {
        _database.HashGetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns([RedisValue.Null, RedisValue.Null]);

        var result = await _sut.GetAsync("statestore", "order-1");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_Should_Return_StateEntry_When_Key_Exists()
    {
        var rawData = Encoding.UTF8.GetBytes("{\"id\":\"ord-1\"}");
        var etag = "etag-123";

        _database.HashGetAsync(
            new RedisKey("centra:state:statestore:order-1"),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns([(RedisValue)rawData, (RedisValue)etag]);

        var result = await _sut.GetAsync("statestore", "order-1");

        result.ShouldNotBeNull();
        result.Value.Key.ShouldBe("order-1");
        result.Value.Value.ShouldBe(rawData);
        result.Value.ETag.ShouldBe(etag);
    }

    [Fact]
    public async Task SetAsync_Should_Call_HashSet_And_KeyExpire_When_Ttl_Provided()
    {
        var payload = Encoding.UTF8.GetBytes("payload-data");
        var options = new StateOptions { TimeToLive = TimeSpan.FromMinutes(5) };

        await _sut.SetAsync("statestore", "order-2", payload, options);

        await _database.Received(1).HashSetAsync(
            new RedisKey("centra:state:statestore:order-2"),
            Arg.Is<HashEntry[]>(entries => entries.Length == 2 && entries.Any(e => e.Name == "data")),
            Arg.Any<CommandFlags>());

        await _database.Received(1).KeyExpireAsync(
            new RedisKey("centra:state:statestore:order-2"),
            TimeSpan.FromMinutes(5),
            Arg.Any<ExpireWhen>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task TrySetAsync_Should_Return_True_When_Script_Returns_1()
    {
        var payload = Encoding.UTF8.GetBytes("payload-data");
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1L));

        var success = await _sut.TrySetAsync("statestore", "order-3", payload, "valid-etag");

        success.ShouldBeTrue();
    }

    [Fact]
    public async Task TrySetAsync_Should_Return_False_When_Script_Returns_0()
    {
        var payload = Encoding.UTF8.GetBytes("payload-data");
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)0L));

        var success = await _sut.TrySetAsync("statestore", "order-3", payload, "stale-etag");

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_Should_Call_KeyDelete()
    {
        await _sut.DeleteAsync("statestore", "order-4");

        await _database.Received(1).KeyDeleteAsync(
            new RedisKey("centra:state:statestore:order-4"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task TryDeleteAsync_Should_Return_True_When_Script_Returns_1()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1L));

        var success = await _sut.TryDeleteAsync("statestore", "order-5", "expected-etag");

        success.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteTransactionAsync_Should_Execute_Batch_Operations()
    {
        var tx = Substitute.For<ITransaction>();
        tx.ExecuteAsync(Arg.Any<CommandFlags>()).Returns(true);
        _database.CreateTransaction(Arg.Any<object>()).Returns(tx);

        var ops = new List<StateTransactionOperation>
        {
            new SetTransactionOperation<byte[]>("key-1", Encoding.UTF8.GetBytes("data-1")),
            new DeleteTransactionOperation("key-2")
        };

        await _sut.ExecuteTransactionAsync("statestore", ops);

        await tx.Received(1).ExecuteAsync();
    }
}
