using System.Net;
using Centra.Locks;
using Centra.Providers.CosmosDb.Documents;
using Centra.Providers.CosmosDb.Locks;
using Centra.Providers.CosmosDb.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Centra.Providers.CosmosDb.Tests.Unit.Locks;

public sealed class CosmosDbDistributedLockDriverTests
{
    private readonly CosmosClient _client = Substitute.For<CosmosClient>();
    private readonly Container _container = Substitute.For<Container>();
    private readonly CosmosDbProviderOptions _options;

    public CosmosDbDistributedLockDriverTests()
    {
        _options = new CosmosDbProviderOptions
        {
            DatabaseName = "test-db",
            LockContainerName = "test-locks",
            LockPartitionKeyPath = "/lockStore",
            AutoCreateDatabaseAndContainers = false,
            Throughput = 400
        };

        _client.GetContainer(_options.DatabaseName, _options.LockContainerName)
            .Returns(_container);
    }

    private CosmosDbDistributedLockDriver CreateSut(CosmosDbProviderOptions? options = null)
    {
        var opts = options ?? _options;
        return new CosmosDbDistributedLockDriver(_client, Microsoft.Extensions.Options.Options.Create(opts));
    }

    [Fact]
    public void Constructor_Should_Throw_When_Client_Is_Null()
    {
        var act = () => new CosmosDbDistributedLockDriver(null!, Microsoft.Extensions.Options.Options.Create(_options));
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("client");
    }

    [Fact]
    public void Constructor_Should_Fallback_To_Default_Options_When_Options_Is_Null()
    {
        var driver = new CosmosDbDistributedLockDriver(_client, null!);
        driver.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquireLockAsync_Should_Throw_When_LockStoreName_Is_Invalid(string? storeName)
    {
        var sut = CreateSut();
        await Should.ThrowAsync<ArgumentException>(async () =>
            await sut.TryAcquireLockAsync(storeName!, "res-1", TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquireLockAsync_Should_Throw_When_ResourceId_Is_Invalid(string? resourceId)
    {
        var sut = CreateSut();
        await Should.ThrowAsync<ArgumentException>(async () =>
            await sut.TryAcquireLockAsync("store-1", resourceId!, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Lock_When_CreateItemAsync_Succeeds()
    {
        var sut = CreateSut();
        var itemResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        itemResponse.ETag.Returns("\"etag-success\"");

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfNoneMatchEtag == "*"),
            Arg.Any<CancellationToken>())
            .Returns(itemResponse);

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30));

        @lock.ShouldNotBeNull();
        @lock.ResourceId.ShouldBe("order-100");
        @lock.LockId.ShouldNotBeNullOrWhiteSpace();
        var cosmosLock = @lock.ShouldBeOfType<CosmosDbDistributedLock>();
        cosmosLock.LockStoreName.ShouldBe("store-1");

        await _container.Received(1).CreateItemAsync(
            Arg.Is<CosmosLockDocument>(d =>
                d.Id == "order-100" &&
                d.LockStore == "store-1" &&
                d.ResourceId == "order-100" &&
                d.TimeToLive == 30 &&
                d.ExpiresAtUtc > d.AcquiredAtUtc),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfNoneMatchEtag == "*"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_AutoCreate_Database_And_Container_When_Enabled()
    {
        _options.AutoCreateDatabaseAndContainers = true;
        var sut = CreateSut(_options);

        var dbResponse = Substitute.For<DatabaseResponse>();
        var database = Substitute.For<Database>();
        dbResponse.Database.Returns(database);

        _client.CreateDatabaseIfNotExistsAsync(
            _options.DatabaseName,
            Arg.Any<int?>(),
            Arg.Any<RequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(dbResponse);

        var containerResponse = Substitute.For<ContainerResponse>();
        database.CreateContainerIfNotExistsAsync(
            Arg.Any<ContainerProperties>(),
            Arg.Any<int?>(),
            Arg.Any<RequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(containerResponse);

        var itemResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        itemResponse.ETag.Returns("\"etag-1\"");
        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(itemResponse);

        var lock1 = await sut.TryAcquireLockAsync("store-1", "order-1", TimeSpan.FromSeconds(30));
        var lock2 = await sut.TryAcquireLockAsync("store-1", "order-2", TimeSpan.FromSeconds(30));

        lock1.ShouldNotBeNull();
        lock2.ShouldNotBeNull();

        // Initialization should only happen once
        await _client.Received(1).CreateDatabaseIfNotExistsAsync(
            _options.DatabaseName,
            Arg.Any<int?>(),
            Arg.Any<RequestOptions>(),
            Arg.Any<CancellationToken>());

        await database.Received(1).CreateContainerIfNotExistsAsync(
            Arg.Is<ContainerProperties>(cp =>
                cp.Id == _options.LockContainerName &&
                cp.PartitionKeyPath == _options.LockPartitionKeyPath &&
                cp.DefaultTimeToLive == -1),
            _options.Throughput,
            Arg.Any<RequestOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Null_When_Conflict_And_Existing_Lock_Not_Expired()
    {
        var sut = CreateSut();
        var conflictException = new CosmosException("Conflict", HttpStatusCode.Conflict, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(conflictException);

        var existingDoc = new CosmosLockDocument
        {
            Id = "order-100",
            LockStore = "store-1",
            ResourceId = "order-100",
            LockId = "existing-lock",
            AcquiredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5), // Still valid!
            TimeToLive = 360
        };

        var readResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        readResponse.Resource.Returns(existingDoc);
        readResponse.ETag.Returns("\"existing-etag\"");

        _container.ReadItemAsync<CosmosLockDocument>(
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(readResponse);

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30));

        @lock.ShouldBeNull();
        await _container.DidNotReceive().ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Replace_And_Return_Lock_When_Conflict_And_Existing_Lock_Is_Expired()
    {
        var sut = CreateSut();
        var conflictException = new CosmosException("Conflict", HttpStatusCode.Conflict, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(conflictException);

        var expiredDoc = new CosmosLockDocument
        {
            Id = "order-100",
            LockStore = "store-1",
            ResourceId = "order-100",
            LockId = "old-expired-lock",
            AcquiredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), // Expired!
            TimeToLive = 300
        };

        var readResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        readResponse.Resource.Returns(expiredDoc);
        readResponse.ETag.Returns("\"expired-etag\"");

        _container.ReadItemAsync<CosmosLockDocument>(
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(readResponse);

        var replaceResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        replaceResponse.ETag.Returns("\"newly-replaced-etag\"");

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == "\"expired-etag\""),
            Arg.Any<CancellationToken>())
            .Returns(replaceResponse);

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30));

        @lock.ShouldNotBeNull();
        @lock.ResourceId.ShouldBe("order-100");
        @lock.LockId.ShouldNotBeNullOrWhiteSpace();
        var cosmosLock = @lock.ShouldBeOfType<CosmosDbDistributedLock>();
        cosmosLock.LockStoreName.ShouldBe("store-1");

        await _container.Received(1).ReplaceItemAsync(
            Arg.Is<CosmosLockDocument>(d =>
                d.Id == "order-100" &&
                d.LockStore == "store-1" &&
                d.ResourceId == "order-100" &&
                d.TimeToLive == 30),
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == "\"expired-etag\""),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Null_When_Expired_Replace_Fails_Due_To_CosmosException()
    {
        var sut = CreateSut();
        var conflictException = new CosmosException("Conflict", HttpStatusCode.Conflict, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(conflictException);

        var expiredDoc = new CosmosLockDocument
        {
            Id = "order-100",
            LockStore = "store-1",
            ResourceId = "order-100",
            LockId = "old-lock",
            AcquiredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            TimeToLive = 300
        };

        var readResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        readResponse.Resource.Returns(expiredDoc);
        readResponse.ETag.Returns("\"expired-etag\"");

        _container.ReadItemAsync<CosmosLockDocument>(
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(readResponse);

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("PreconditionFailed", HttpStatusCode.PreconditionFailed, 0, "act-2", 0));

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30));

        @lock.ShouldBeNull();
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Null_When_ReadItem_Throws_CosmosException()
    {
        var sut = CreateSut();
        var conflictException = new CosmosException("Conflict", HttpStatusCode.Conflict, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(conflictException);

        _container.ReadItemAsync<CosmosLockDocument>(
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("NotFound", HttpStatusCode.NotFound, 0, "act-2", 0));

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30));

        @lock.ShouldBeNull();
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Return_Null_When_ReadItem_Returns_Null_Resource()
    {
        var sut = CreateSut();
        var conflictException = new CosmosException("Conflict", HttpStatusCode.Conflict, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(conflictException);

        var readResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        readResponse.Resource.Returns((CosmosLockDocument?)null);

        _container.ReadItemAsync<CosmosLockDocument>(
            "order-100",
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("store-1"))),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(readResponse);

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30));

        @lock.ShouldBeNull();
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Rethrow_When_CreateItem_Throws_Non_Conflict_CosmosException()
    {
        var sut = CreateSut();
        var serviceUnavailableException = new CosmosException("Service Unavailable", HttpStatusCode.ServiceUnavailable, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(serviceUnavailableException);

        await Should.ThrowAsync<CosmosException>(async () =>
            await sut.TryAcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task AcquireLockAsync_Should_Return_Lock_When_Available()
    {
        var sut = CreateSut();
        var itemResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        itemResponse.ETag.Returns("\"etag-acquire\"");

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(itemResponse);

        var @lock = await sut.AcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));

        @lock.ShouldNotBeNull();
        @lock.ResourceId.ShouldBe("order-100");
    }

    [Fact]
    public async Task AcquireLockAsync_Should_Throw_TimeoutException_When_Lock_Cannot_Be_Acquired()
    {
        var sut = CreateSut();
        var conflictException = new CosmosException("Conflict", HttpStatusCode.Conflict, 0, "act-1", 0);

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(conflictException);

        var existingDoc = new CosmosLockDocument
        {
            Id = "order-100",
            LockStore = "store-1",
            ResourceId = "order-100",
            LockId = "held-lock",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var readResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        readResponse.Resource.Returns(existingDoc);

        _container.ReadItemAsync<CosmosLockDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(readResponse);

        await Should.ThrowAsync<TimeoutException>(async () =>
            await sut.AcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task AcquireLockAsync_Should_Throw_OperationCanceledException_When_Cancellation_Requested()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await sut.AcquireLockAsync("store-1", "order-100", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Not_AutoCreate_When_AutoCreate_Is_False()
    {
        _options.AutoCreateDatabaseAndContainers = false;
        var sut = CreateSut(_options);

        var itemResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        itemResponse.ETag.Returns("\"etag-1\"");
        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(itemResponse);

        await sut.TryAcquireLockAsync("store-1", "order-1", TimeSpan.FromSeconds(30));

        await _client.DidNotReceive().CreateDatabaseIfNotExistsAsync(
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<RequestOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryAcquireLockAsync_Should_Pass_CancellationToken_To_CreateItemAsync()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var itemResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        itemResponse.ETag.Returns("\"etag-token\"");

        _container.CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            cts.Token)
            .Returns(itemResponse);

        var @lock = await sut.TryAcquireLockAsync("store-1", "order-token", TimeSpan.FromSeconds(30), cts.Token);

        @lock.ShouldNotBeNull();
        await _container.Received(1).CreateItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            cts.Token);
    }
}
