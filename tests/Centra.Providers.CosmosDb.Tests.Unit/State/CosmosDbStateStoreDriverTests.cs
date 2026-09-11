using System.Net;
using System.Text;
using Centra.Providers.CosmosDb.Documents;
using Centra.Providers.CosmosDb.Options;
using Centra.Providers.CosmosDb.State;
using Centra.State;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Centra.Providers.CosmosDb.Tests.Unit.State;

public sealed class CosmosDbStateStoreDriverTests
{
    private readonly CosmosClient _client = Substitute.For<CosmosClient>();
    private readonly Container _container = Substitute.For<Container>();
    private readonly CosmosDbProviderOptions _options;

    public CosmosDbStateStoreDriverTests()
    {
        _options = new CosmosDbProviderOptions
        {
            DatabaseName = "test-db",
            StateContainerName = "test-state",
            StatePartitionKeyPath = "/storeName",
            AutoCreateDatabaseAndContainers = false,
            Throughput = 400
        };

        _client.GetContainer(_options.DatabaseName, _options.StateContainerName)
            .Returns(_container);
    }

    private CosmosDbStateStoreDriver CreateSut(CosmosDbProviderOptions? options = null)
    {
        var opts = options ?? _options;
        return new CosmosDbStateStoreDriver(_client, Microsoft.Extensions.Options.Options.Create(opts));
    }

    [Fact]
    public void Constructor_Should_Throw_When_Client_Is_Null()
    {
        var act = () => new CosmosDbStateStoreDriver(null!, Microsoft.Extensions.Options.Options.Create(_options));
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("client");
    }

    [Fact]
    public void Constructor_Should_Fallback_To_Default_Options_When_Options_Is_Null()
    {
        var driver = new CosmosDbStateStoreDriver(_client, null!);
        driver.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null, "key1")]
    [InlineData("", "key1")]
    [InlineData("   ", "key1")]
    [InlineData("store1", null)]
    [InlineData("store1", "")]
    [InlineData("store1", "   ")]
    public async Task GetAsync_Should_Throw_On_Invalid_Arguments(string? store, string? key)
    {
        var sut = CreateSut();
        await Should.ThrowAsync<ArgumentException>(() => sut.GetAsync(store!, key!).AsTask());
    }

    [Fact]
    public async Task GetAsync_Should_Return_Null_When_NotFound()
    {
        var sut = CreateSut();
        _container.ReadItemAsync<CosmosStateDocument>(
            "item-404",
            Arg.Any<PartitionKey>(),
            null,
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("NotFound", HttpStatusCode.NotFound, 0, "act-1", 0));

        var result = await sut.GetAsync("store-1", "item-404");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_Should_Return_StateEntry_When_Item_Exists()
    {
        var sut = CreateSut();
        var payloadBytes = Encoding.UTF8.GetBytes("hello-cosmos");
        var doc = new CosmosStateDocument
        {
            Id = "key-1",
            StoreName = "store-1",
            Key = "key-1",
            Value = Convert.ToBase64String(payloadBytes),
            ETag = "\"etag-123\""
        };

        var response = Substitute.For<ItemResponse<CosmosStateDocument>>();
        response.Resource.Returns(doc);
        response.ETag.Returns("\"etag-123\"");

        _container.ReadItemAsync<CosmosStateDocument>(
            "key-1",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            null,
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await sut.GetAsync("store-1", "key-1");

        result.ShouldNotBeNull();
        result.Value.Key.ShouldBe("key-1");
        result.Value.Value.ShouldBe(payloadBytes);
        result.Value.ETag.ShouldBe("\"etag-123\"");
    }

    [Fact]
    public async Task SetAsync_Should_Upsert_Document_With_TTL()
    {
        var sut = CreateSut();
        var data = Encoding.UTF8.GetBytes("test-val");
        var options = new StateOptions { TimeToLive = TimeSpan.FromSeconds(60) };

        await sut.SetAsync("store-1", "key-ttl", data, options);

        await _container.Received(1).UpsertItemAsync(
            Arg.Is<CosmosStateDocument>(d =>
                d.Id == "key-ttl" &&
                d.StoreName == "store-1" &&
                d.Key == "key-ttl" &&
                d.Value == Convert.ToBase64String(data) &&
                d.TimeToLive == 60),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySetAsync_Should_Return_True_When_Replace_Succeeds()
    {
        var sut = CreateSut();
        var data = Encoding.UTF8.GetBytes("new-val");
        var response = Substitute.For<ItemResponse<CosmosStateDocument>>();

        _container.ReplaceItemAsync(
            Arg.Any<CosmosStateDocument>(),
            "key-cas",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Is<ItemRequestOptions>(o => o.IfMatchEtag == "\"valid-etag\""),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var success = await sut.TrySetAsync("store-1", "key-cas", data, "\"valid-etag\"");

        success.ShouldBeTrue();
    }

    [Fact]
    public async Task TrySetAsync_Should_Return_False_When_PreconditionFailed()
    {
        var sut = CreateSut();
        var data = Encoding.UTF8.GetBytes("new-val");

        _container.ReplaceItemAsync(
            Arg.Any<CosmosStateDocument>(),
            "key-cas",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("PreconditionFailed", HttpStatusCode.PreconditionFailed, 0, "act-1", 0));

        var success = await sut.TrySetAsync("store-1", "key-cas", data, "\"stale-etag\"");

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_Should_Ignore_NotFound_Exception()
    {
        var sut = CreateSut();

        _container.DeleteItemAsync<CosmosStateDocument>(
            "key-del",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            null,
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("NotFound", HttpStatusCode.NotFound, 0, "act-1", 0));

        await sut.DeleteAsync("store-1", "key-del");

        // Does not throw
    }

    [Fact]
    public async Task TryDeleteAsync_Should_Return_True_When_Delete_Succeeds()
    {
        var sut = CreateSut();
        var response = Substitute.For<ItemResponse<CosmosStateDocument>>();

        _container.DeleteItemAsync<CosmosStateDocument>(
            "key-try-del",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Is<ItemRequestOptions>(o => o.IfMatchEtag == "\"etag-1\""),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var success = await sut.TryDeleteAsync("store-1", "key-try-del", "\"etag-1\"");

        success.ShouldBeTrue();
    }

    [Fact]
    public async Task TryDeleteAsync_Should_Return_False_When_PreconditionFailed()
    {
        var sut = CreateSut();

        _container.DeleteItemAsync<CosmosStateDocument>(
            "key-try-del",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("PreconditionFailed", HttpStatusCode.PreconditionFailed, 0, "act-1", 0));

        var success = await sut.TryDeleteAsync("store-1", "key-try-del", "\"stale-etag\"");

        success.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteTransactionAsync_Should_Return_Early_When_Empty()
    {
        var sut = CreateSut();

        await sut.ExecuteTransactionAsync("store-1", Array.Empty<StateTransactionOperation>());

        _container.DidNotReceive().CreateTransactionalBatch(Arg.Any<PartitionKey>());
    }

    [Fact]
    public async Task ExecuteTransactionAsync_Should_Execute_Batch_Operations()
    {
        var sut = CreateSut();
        var batch = Substitute.For<TransactionalBatch>();
        var batchResponse = Substitute.For<TransactionalBatchResponse>();
        batchResponse.IsSuccessStatusCode.Returns(true);

        _container.CreateTransactionalBatch(Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))))
            .Returns(batch);

        batch.ExecuteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(batchResponse));

        var ops = new StateTransactionOperation[]
        {
            new SetTransactionOperation<byte[]>("k1", Encoding.UTF8.GetBytes("v1"), Options: new StateOptions { TimeToLive = TimeSpan.FromSeconds(10) }),
            new DeleteTransactionOperation("k2")
        };

        await sut.ExecuteTransactionAsync("store-1", ops);

        batch.Received(1).UpsertItem(Arg.Is<CosmosStateDocument>(d => d.Key == "k1" && d.TimeToLive == 10));
        batch.Received(1).DeleteItem("k2");
        await batch.Received(1).ExecuteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTransactionAsync_Should_Throw_When_Batch_Fails()
    {
        var sut = CreateSut();
        var batch = Substitute.For<TransactionalBatch>();
        var batchResponse = Substitute.For<TransactionalBatchResponse>();
        batchResponse.IsSuccessStatusCode.Returns(false);
        batchResponse.StatusCode.Returns(HttpStatusCode.Conflict);
        batchResponse.ErrorMessage.Returns("Conflict occurred");

        _container.CreateTransactionalBatch(Arg.Any<PartitionKey>())
            .Returns(batch);

        batch.ExecuteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(batchResponse));

        var ops = new StateTransactionOperation[] { new DeleteTransactionOperation("k1") };

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.ExecuteTransactionAsync("store-1", ops).AsTask());

        ex.Message.ShouldContain("Conflict occurred");
    }

    [Fact]
    public async Task EnsureInitializedAsync_Should_AutoCreate_Database_And_Containers_When_Enabled()
    {
        var options = new CosmosDbProviderOptions
        {
            DatabaseName = "init-db",
            StateContainerName = "init-state",
            StatePartitionKeyPath = "/storeName",
            AutoCreateDatabaseAndContainers = true,
            Throughput = 400
        };

        _client.GetContainer("init-db", "init-state").Returns(_container);

        var dbResponse = Substitute.For<DatabaseResponse>();
        var database = Substitute.For<Database>();
        dbResponse.Database.Returns(database);

        _client.CreateDatabaseIfNotExistsAsync("init-db", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dbResponse));

        var containerResponse = Substitute.For<ContainerResponse>();
        database.CreateContainerIfNotExistsAsync(
            Arg.Any<ContainerProperties>(),
            options.Throughput,
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(containerResponse));

        var sut = CreateSut(options);

        // Call SetAsync to trigger initialization
        await sut.SetAsync("store-1", "k1", Encoding.UTF8.GetBytes("v1"));

        await _client.Received(1).CreateDatabaseIfNotExistsAsync("init-db", cancellationToken: Arg.Any<CancellationToken>());
        await database.Received(1).CreateContainerIfNotExistsAsync(
            Arg.Is<ContainerProperties>(cp => cp.Id == "init-state" && cp.PartitionKeyPath == "/storeName"),
            400,
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
