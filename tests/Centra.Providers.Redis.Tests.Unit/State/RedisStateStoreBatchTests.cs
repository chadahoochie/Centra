using System.Text;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.State;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.State;

public sealed class RedisStateStoreBatchTests
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly IBatch _batch;
    private readonly RedisStateStoreDriver _sut;

    public RedisStateStoreBatchTests()
    {
        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _database = Substitute.For<IDatabase>();
        _batch = Substitute.For<IBatch>();
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _database.CreateBatch().Returns(_batch);

        var options = new RedisProviderOptions { KeyPrefix = "centra:" };
        _sut = new RedisStateStoreDriver(_multiplexer, Microsoft.Extensions.Options.Options.Create(options));
    }

    [Fact]
    public async Task GetBatchAsync_Pipelines_Batch_And_Returns_Found_Entries()
    {
        // Arrange
        var key1 = "ord-1";
        var key2 = "ord-2";
        var data1 = Encoding.UTF8.GetBytes("data-1");

        _batch.HashGetAsync(
            (RedisKey)"centra:state:mystore:ord-1",
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new RedisValue[] { (RedisValue)data1, (RedisValue)"etag-1" }));

        _batch.HashGetAsync(
            (RedisKey)"centra:state:mystore:ord-2",
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new RedisValue[] { RedisValue.Null, RedisValue.Null }));

        // Act
        var results = await _sut.GetBatchAsync("mystore", [key1, key2]);

        // Assert
        _database.Received(1).CreateBatch();
        _batch.Received(1).Execute();
        results.Count.ShouldBe(1);
        results[0].Key.ShouldBe("ord-1");
        results[0].Value.ShouldBe(data1);
        results[0].ETag.ShouldBe("etag-1");
    }

    [Fact]
    public async Task GetBatchAsync_ReturnsEmpty_WhenKeysEmpty()
    {
        // Act
        var results = await _sut.GetBatchAsync("mystore", Array.Empty<string>());

        // Assert
        results.ShouldBeEmpty();
        _database.DidNotReceive().CreateBatch();
    }
}
