using System.Text;
using Centra.Providers.InMemory.State;
using Centra.State;
using Centra.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Providers;

public sealed class InMemoryStateStoreDriverTests
{
    private readonly InMemoryStateStoreDriver _driver = new();

    [Theory, AutoNSubstituteData]
    public async Task Should_Set_And_Get_State_With_Valid_ETag(string store, string key, string valueStr)
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(valueStr);

        // Act
        await _driver.SetAsync(store, key, bytes);
        var entry = await _driver.GetAsync(store, key);

        // Assert
        entry.ShouldNotBeNull();
        entry.Value.Key.ShouldBe(key);
        entry.Value.ETag.ShouldNotBeNullOrWhiteSpace();
        Encoding.UTF8.GetString(entry.Value.Value).ShouldBe(valueStr);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Enforce_Optimistic_Concurrency_On_TrySet(string store, string key, string val1, string val2)
    {
        // Arrange
        var bytes1 = Encoding.UTF8.GetBytes(val1);
        var bytes2 = Encoding.UTF8.GetBytes(val2);

        await _driver.SetAsync(store, key, bytes1);
        var initialEntry = await _driver.GetAsync(store, key);
        initialEntry.ShouldNotBeNull();

        // Act 1: Success with correct ETag
        var updated = await _driver.TrySetAsync(store, key, bytes2, initialEntry.Value.ETag);
        updated.ShouldBeTrue();

        // Act 2: Failure with stale ETag
        var staleUpdate = await _driver.TrySetAsync(store, key, bytes1, initialEntry.Value.ETag);
        staleUpdate.ShouldBeFalse();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Enforce_Optimistic_Concurrency_On_TryDelete(string store, string key, string val)
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(val);
        await _driver.SetAsync(store, key, bytes);
        var entry = await _driver.GetAsync(store, key);
        entry.ShouldNotBeNull();

        // Act & Assert
        var wrongEtagResult = await _driver.TryDeleteAsync(store, key, "wrong-etag");
        wrongEtagResult.ShouldBeFalse();

        var correctResult = await _driver.TryDeleteAsync(store, key, entry.Value.ETag);
        correctResult.ShouldBeTrue();

        var afterDelete = await _driver.GetAsync(store, key);
        afterDelete.ShouldBeNull();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Execute_Transactions_Atomically(string store, string key1, string key2, string val1, string val2)
    {
        // Arrange
        var bytes1 = Encoding.UTF8.GetBytes(val1);
        var bytes2 = Encoding.UTF8.GetBytes(val2);

        var ops = new StateTransactionOperation[]
        {
            new SetTransactionOperation<byte[]>(key1, bytes1),
            new SetTransactionOperation<byte[]>(key2, bytes2)
        };

        // Act
        await _driver.ExecuteTransactionAsync(store, ops);

        // Assert
        var e1 = await _driver.GetAsync(store, key1);
        var e2 = await _driver.GetAsync(store, key2);

        e1.ShouldNotBeNull();
        e2.ShouldNotBeNull();
        Encoding.UTF8.GetString(e1.Value.Value).ShouldBe(val1);
        Encoding.UTF8.GetString(e2.Value.Value).ShouldBe(val2);
    }
}
