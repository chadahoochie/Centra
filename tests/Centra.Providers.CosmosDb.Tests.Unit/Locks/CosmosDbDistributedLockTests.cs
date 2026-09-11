using System.Net;
using Centra.Locks;
using Centra.Providers.CosmosDb.Documents;
using Centra.Providers.CosmosDb.Locks;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Centra.Providers.CosmosDb.Tests.Unit.Locks;

public sealed class CosmosDbDistributedLockTests
{
    private readonly Container _container = Substitute.For<Container>();
    private const string LockStoreName = "test-store";
    private const string ResourceId = "order-456";
    private const string LockId = "guid-123";
    private const string InitialETag = "\"initial-etag\"";

    private CosmosDbDistributedLock CreateSut(string etag = InitialETag) =>
        new(_container, LockStoreName, ResourceId, LockId, etag);

    [Fact]
    public void Constructor_Should_Throw_When_Container_Is_Null()
    {
        var act = () => new CosmosDbDistributedLock(null!, LockStoreName, ResourceId, LockId, InitialETag);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("container");
    }

    [Fact]
    public void Constructor_Should_Throw_When_LockStoreName_Is_Null()
    {
        var act = () => new CosmosDbDistributedLock(_container, null!, ResourceId, LockId, InitialETag);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("lockStoreName");
    }

    [Fact]
    public void Constructor_Should_Throw_When_ResourceId_Is_Null()
    {
        var act = () => new CosmosDbDistributedLock(_container, LockStoreName, null!, LockId, InitialETag);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("resourceId");
    }

    [Fact]
    public void Constructor_Should_Throw_When_LockId_Is_Null()
    {
        var act = () => new CosmosDbDistributedLock(_container, LockStoreName, ResourceId, null!, InitialETag);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("lockId");
    }

    [Fact]
    public void Constructor_Should_Throw_When_InitialETag_Is_Null()
    {
        var act = () => new CosmosDbDistributedLock(_container, LockStoreName, ResourceId, LockId, null!);
        act.ShouldThrow<ArgumentNullException>().ParamName.ShouldBe("initialETag");
    }

    [Fact]
    public void Constructor_Should_Set_Properties_Correctly()
    {
        var sut = CreateSut();
        sut.LockStoreName.ShouldBe(LockStoreName);
        sut.ResourceId.ShouldBe(ResourceId);
        sut.LockId.ShouldBe(LockId);
    }

    [Fact]
    public async Task RenewAsync_Should_Return_True_And_Update_ETag_When_Replace_Succeeds()
    {
        var sut = CreateSut();
        var newETag = "\"new-etag\"";
        var response = Substitute.For<ItemResponse<CosmosLockDocument>>();
        response.ETag.Returns(newETag);

        _container.ReplaceItemAsync(
            Arg.Is<CosmosLockDocument>(d =>
                d.Id == ResourceId &&
                d.LockStore == LockStoreName &&
                d.ResourceId == ResourceId &&
                d.LockId == LockId &&
                d.TimeToLive == 30),
            Arg.Is<string>(id => id == ResourceId),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(LockStoreName))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == InitialETag),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await sut.RenewAsync(TimeSpan.FromSeconds(30));

        result.ShouldBeTrue();

        // Second renew should use the new ETag
        var secondETag = "\"second-etag\"";
        var secondResponse = Substitute.For<ItemResponse<CosmosLockDocument>>();
        secondResponse.ETag.Returns(secondETag);

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Is<string>(id => id == ResourceId),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(LockStoreName))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == newETag),
            Arg.Any<CancellationToken>())
            .Returns(secondResponse);

        var secondResult = await sut.RenewAsync(TimeSpan.FromSeconds(60));
        secondResult.ShouldBeTrue();
    }

    [Fact]
    public async Task RenewAsync_Should_Keep_Current_ETag_When_Response_ETag_Is_Null()
    {
        var sut = CreateSut();
        var response = Substitute.For<ItemResponse<CosmosLockDocument>>();
        response.ETag.Returns((string?)null);

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Is<string>(id => id == ResourceId),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(LockStoreName))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == InitialETag),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await sut.RenewAsync(TimeSpan.FromSeconds(30));

        result.ShouldBeTrue();

        // Next call should still use InitialETag since response.ETag was null
        await sut.RenewAsync(TimeSpan.FromSeconds(30));
        await _container.Received(2).ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Is<string>(id => id == ResourceId),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(LockStoreName))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == InitialETag),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenewAsync_Should_Enforce_Minimum_One_Second_TTL()
    {
        var sut = CreateSut();
        var response = Substitute.For<ItemResponse<CosmosLockDocument>>();
        response.ETag.Returns("\"some-etag\"");

        _container.ReplaceItemAsync(
            Arg.Is<CosmosLockDocument>(d => d.TimeToLive == 1),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await sut.RenewAsync(TimeSpan.FromMilliseconds(100));

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task RenewAsync_Should_Return_False_When_PreconditionFailed()
    {
        var sut = CreateSut();
        var cosmosException = new CosmosException("Precondition failed", HttpStatusCode.PreconditionFailed, 0, "activityId", 0);

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(cosmosException);

        var result = await sut.RenewAsync(TimeSpan.FromSeconds(30));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RenewAsync_Should_Return_False_When_NotFound()
    {
        var sut = CreateSut();
        var cosmosException = new CosmosException("Not found", HttpStatusCode.NotFound, 0, "activityId", 0);

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(cosmosException);

        var result = await sut.RenewAsync(TimeSpan.FromSeconds(30));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RenewAsync_Should_Rethrow_When_Other_CosmosException()
    {
        var sut = CreateSut();
        var cosmosException = new CosmosException("Service unavailable", HttpStatusCode.ServiceUnavailable, 0, "activityId", 0);

        _container.ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(cosmosException);

        await Should.ThrowAsync<CosmosException>(async () => await sut.RenewAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task RenewAsync_Should_Return_False_When_Already_Disposed()
    {
        var sut = CreateSut();
        await sut.DisposeAsync();

        var result = await sut.RenewAsync(TimeSpan.FromSeconds(30));

        result.ShouldBeFalse();
        await _container.DidNotReceive().ReplaceItemAsync(
            Arg.Any<CosmosLockDocument>(),
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_Should_Delete_Item_With_Current_ETag()
    {
        var sut = CreateSut();

        await sut.DisposeAsync();

        await _container.Received(1).DeleteItemAsync<CosmosLockDocument>(
            Arg.Is<string>(id => id == ResourceId),
            Arg.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(LockStoreName))),
            Arg.Is<ItemRequestOptions>(opts => opts.IfMatchEtag == InitialETag),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_Should_Only_Delete_Once_When_Called_Multiple_Times()
    {
        var sut = CreateSut();

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        await _container.Received(1).DeleteItemAsync<CosmosLockDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_Should_Suppress_Exceptions()
    {
        var sut = CreateSut();
        _container.DeleteItemAsync<CosmosLockDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            Arg.Any<ItemRequestOptions>(),
            Arg.Any<CancellationToken>())
            .Throws(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "activityId", 0));

        // Should not throw
        await sut.DisposeAsync();
    }
}
