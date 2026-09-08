using System;
using System.Threading;
using System.Threading.Tasks;
using Centra.Drivers;
using Centra.Registry;
using Centra.Resilience;
using Centra.Serialization;
using Centra.State;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.State;

public sealed class StateStoreExtendedTests
{
    private readonly ComponentRegistry _registry;
    private readonly IStateStoreDriver _driver;
    private readonly CentraStateStore _sut;
    private const string StoreName = "test-store";

    public StateStoreExtendedTests()
    {
        _registry = new ComponentRegistry();
        _driver = Substitute.For<IStateStoreDriver>();
        _registry.RegisterStateStoreDriver(StoreName, _driver);
        
        // Arrange SUT
        _sut = new CentraStateStore(_registry, JsonCentraSerializer.Default);
    }

    [Fact]
    public async Task Should_Delete_State_And_Call_Driver()
    {
        // Arrange
        var key = "order-123";

        // Act
        await _sut.DeleteAsync(StoreName, key);

        // Assert
        await _driver.Received(1).DeleteAsync(
            StoreName, 
            key, 
            Arg.Any<StateOptions?>(), 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TryDelete_With_ETag_Return_True_When_Successful()
    {
        // Arrange
        var key = "order-123";
        var etag = "etag-1";
        
        _driver.TryDeleteAsync(StoreName, key, etag, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        // Act
        var result = await _sut.TryDeleteAsync(StoreName, key, etag);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_TryDelete_With_ETag_Return_False_When_Conflict()
    {
        // Arrange
        var key = "order-123";
        var etag = "etag-1";
        
        _driver.TryDeleteAsync(StoreName, key, etag, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(false));

        // Act
        var result = await _sut.TryDeleteAsync(StoreName, key, etag);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Throw_InvalidOperationException_When_Store_Not_Registered()
    {
        // Arrange
        var invalidStore = "missing-store";
        
        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () => 
            await _sut.GetAsync<TestStateOrder>(invalidStore, "key"));
    }

    [Fact]
    public async Task Should_Rethrow_And_Record_Error_When_Get_Throws()
    {
        // Arrange
        var key = "order-123";
        
        _driver.GetAsync(StoreName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Driver error"));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () => 
            await _sut.GetAsync<TestStateOrder>(StoreName, key));
    }

    [Fact]
    public async Task Should_Rethrow_And_Record_Error_When_Set_Throws()
    {
        // Arrange
        var key = "order-123";
        var state = new TestStateOrder("1", "A", 1);
        
        _driver.SetAsync(StoreName, key, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("Driver error")));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () => 
            await _sut.SetAsync(StoreName, key, state));
    }

    [Fact]
    public async Task Should_Rethrow_And_Record_Error_When_Delete_Throws()
    {
        // Arrange
        var key = "order-123";
        
        _driver.DeleteAsync(StoreName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("Driver error")));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () => 
            await _sut.DeleteAsync(StoreName, key));
    }

    [Fact]
    public async Task Should_Return_Null_When_Deserialized_Value_Is_Null()
    {
        // Arrange
        var key = "order-123";
        // 'null' as UTF-8 bytes to simulate json null
        var stateItem = new StateEntry<byte[]>(key, "null"u8.ToArray(), "etag-1"); 
        
        _driver.GetAsync(StoreName, key, Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)stateItem));

        // Act
        var result = await _sut.GetAsync<TestStateOrder>(StoreName, key);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Execute_Via_Resilience_Pipeline_When_Provider_Configured()
    {
        // Arrange
        var resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        var pipeline = Substitute.For<IResiliencePipeline>();
        resilienceProvider.GetStateStorePipeline(StoreName).Returns(pipeline);

        pipeline.ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var callback = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                var ct = callInfo.Arg<CancellationToken>();
                return callback(ct);
            });

        var store = new CentraStateStore(_registry, JsonCentraSerializer.Default, resilienceProvider);

        // Act
        await store.DeleteAsync(StoreName, "order-resilience");

        // Assert
        await pipeline.Received(1).ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>());
        await _driver.Received(1).DeleteAsync(StoreName, "order-resilience", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>());
    }
}
