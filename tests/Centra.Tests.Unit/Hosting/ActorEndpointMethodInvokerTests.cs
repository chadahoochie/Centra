using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.Hosting.Routing;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class TestEndpointActor
{
    public ValueTask<string> GetNameAsync() => ValueTask.FromResult("TestActor");
    public Task<int> ComputeAsync(int value) => Task.FromResult(value * 2);
    public ValueTask DoWorkAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public Task<string> ProcessAsync(string input, CancellationToken ct) => Task.FromResult($"Processed: {input}");
    public string GetSyncValue() => "sync-result";
    public Task DoTaskWorkAsync() => Task.CompletedTask;
    public ValueTask<string> MultiParamAsync(string a, CancellationToken ct) => ValueTask.FromResult(a);
}

public sealed class ActorEndpointMethodInvokerTests
{
    [Fact]
    public void Should_Return_Null_For_Missing_Method()
    {
        // Arrange
        var type = typeof(TestEndpointActor);

        // Act
        var result = ActorEndpointMethodInvoker.GetOrCreate(type, "MissingMethod");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Invoke_Parameterless_ValueTask_Method()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.GetNameAsync));
        
        invoker.ShouldNotBeNull();

        // Act
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        // Assert
        result.ShouldBe("TestActor");
    }

    [Fact]
    public async Task Should_Invoke_Method_With_Body_Parameter()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ComputeAsync));
        
        invoker.ShouldNotBeNull();
        
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(10);

        // Act
        var result = await invoker.InvokeAsync(actor, bodyBytes, CancellationToken.None);

        // Assert
        result.ShouldBe(20);
    }

    [Fact]
    public async Task Should_Invoke_Method_With_CancellationToken_Only()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.DoWorkAsync));
        
        invoker.ShouldNotBeNull();
        
        using var cts = new CancellationTokenSource();

        // Act
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, cts.Token);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Invoke_Method_With_Body_And_CancellationToken()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ProcessAsync));
        
        invoker.ShouldNotBeNull();
        
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes("hello");
        using var cts = new CancellationTokenSource();

        // Act
        var result = await invoker.InvokeAsync(actor, bodyBytes, cts.Token);

        // Assert
        result.ShouldBe("Processed: hello");
    }

    [Fact]
    public async Task Should_Invoke_Sync_Return_Method()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.GetSyncValue));
        
        invoker.ShouldNotBeNull();

        // Act
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        // Assert
        result.ShouldBe("sync-result");
    }

    [Fact]
    public async Task Should_Invoke_Task_Void_Method()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.DoTaskWorkAsync));
        
        invoker.ShouldNotBeNull();

        // Act
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Should_Cache_Invoker_For_Same_Type_And_Method()
    {
        // Arrange
        var type = typeof(TestEndpointActor);

        // Act
        var invoker1 = ActorEndpointMethodInvoker.GetOrCreate(type, nameof(TestEndpointActor.GetNameAsync));
        var invoker2 = ActorEndpointMethodInvoker.GetOrCreate(type, nameof(TestEndpointActor.GetNameAsync));

        // Assert
        invoker1.ShouldNotBeNull();
        invoker2.ShouldNotBeNull();
        invoker1.ShouldBeSameAs(invoker2);
    }

    [Fact]
    public async Task Should_Handle_Empty_Body_For_Value_Type_Parameter()
    {
        // Arrange
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ComputeAsync));
        
        invoker.ShouldNotBeNull();

        // Act
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        // Assert
        result.ShouldBe(0);
    }
}
