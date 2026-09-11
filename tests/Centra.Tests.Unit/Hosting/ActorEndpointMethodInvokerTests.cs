using System.Text.Json;
using Centra.Hosting.Routing;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class ActorEndpointMethodInvokerTests
{
    private sealed class TestEndpointActor
    {
        public ValueTask<string> GetNameAsync() => ValueTask.FromResult("TestActor");
        public Task<int> ComputeAsync(int value) => Task.FromResult(value * 2);
        public ValueTask DoWorkAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public Task<string> ProcessAsync(string input, CancellationToken ct) => Task.FromResult($"Processed: {input}");
        public string GetSyncValue() => "sync-result";
        public Task DoTaskWorkAsync() => Task.CompletedTask;
        public ValueTask<string> MultiParamAsync(string a, CancellationToken ct) => ValueTask.FromResult(a);
        public void ThrowSync() => throw new InvalidOperationException("Sync throw");
        public void ThrowWithCt(CancellationToken ct) => throw new InvalidOperationException("Ct throw");
        public void ThrowWithParam(string input) => throw new InvalidOperationException("Param throw");
        public void ThrowWithParamAndCt(string input, CancellationToken ct) => throw new InvalidOperationException("Param and Ct throw");
        public void TooManyParams(string a, string b, string c) { }
    }

    [Fact]
    public void Should_Return_Null_For_Missing_Method()
    {
        var type = typeof(TestEndpointActor);
        var result = ActorEndpointMethodInvoker.GetOrCreate(type, "MissingMethod");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Invoke_Parameterless_ValueTask_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.GetNameAsync));
        
        invoker.ShouldNotBeNull();
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        result.ShouldBe("TestActor");
    }

    [Fact]
    public async Task Should_Invoke_Method_With_Body_Parameter()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ComputeAsync));
        
        invoker.ShouldNotBeNull();
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(10);
        var result = await invoker.InvokeAsync(actor, bodyBytes, CancellationToken.None);
        result.ShouldBe(20);
    }

    [Fact]
    public async Task Should_Invoke_Method_With_CancellationToken_Only()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.DoWorkAsync));
        
        invoker.ShouldNotBeNull();
        using var cts = new CancellationTokenSource();
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, cts.Token);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Invoke_Method_With_Body_And_CancellationToken()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ProcessAsync));
        
        invoker.ShouldNotBeNull();
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes("hello");
        using var cts = new CancellationTokenSource();
        var result = await invoker.InvokeAsync(actor, bodyBytes, cts.Token);
        result.ShouldBe("Processed: hello");
    }

    [Fact]
    public async Task Should_Invoke_Sync_Return_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.GetSyncValue));
        
        invoker.ShouldNotBeNull();
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        result.ShouldBe("sync-result");
    }

    [Fact]
    public async Task Should_Invoke_Task_Void_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.DoTaskWorkAsync));
        
        invoker.ShouldNotBeNull();
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        result.ShouldBeNull();
    }

    [Fact]
    public void Should_Cache_Invoker_For_Same_Type_And_Method()
    {
        var type = typeof(TestEndpointActor);
        var invoker1 = ActorEndpointMethodInvoker.GetOrCreate(type, nameof(TestEndpointActor.GetNameAsync));
        var invoker2 = ActorEndpointMethodInvoker.GetOrCreate(type, nameof(TestEndpointActor.GetNameAsync));

        invoker1.ShouldNotBeNull();
        invoker2.ShouldNotBeNull();
        invoker1.ShouldBeSameAs(invoker2);
    }

    [Fact]
    public async Task Should_Handle_Empty_Body_For_Value_Type_Parameter()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ComputeAsync));
        
        invoker.ShouldNotBeNull();
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        result.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Handle_Empty_Body_For_Reference_Type_Parameter()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ProcessAsync));
        
        invoker.ShouldNotBeNull();
        var result = await invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        result.ShouldBe("Processed: ");
    }

    [Fact]
    public async Task Should_Invoke_General_Fallback_With_More_Than_Two_Parameters()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.TooManyParams));
        
        invoker.ShouldNotBeNull();
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes("paramA");
        using var cts = new CancellationTokenSource();
        var result = await invoker.InvokeAsync(actor, bodyBytes, cts.Token);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Unwrap_Exception_From_Parameterless_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ThrowSync));
        
        invoker.ShouldNotBeNull();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None).AsTask());
        ex.Message.ShouldBe("Sync throw");
    }

    [Fact]
    public async Task Should_Unwrap_Exception_From_CtOnly_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ThrowWithCt));
        
        invoker.ShouldNotBeNull();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None).AsTask());
        ex.Message.ShouldBe("Ct throw");
    }

    [Fact]
    public async Task Should_Unwrap_Exception_From_SingleParam_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ThrowWithParam));
        
        invoker.ShouldNotBeNull();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None).AsTask());
        ex.Message.ShouldBe("Param throw");
    }

    [Fact]
    public async Task Should_Unwrap_Exception_From_TwoParams_Method()
    {
        var actor = new TestEndpointActor();
        var invoker = ActorEndpointMethodInvoker.GetOrCreate(typeof(TestEndpointActor), nameof(TestEndpointActor.ThrowWithParamAndCt));
        
        invoker.ShouldNotBeNull();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync(actor, ReadOnlyMemory<byte>.Empty, CancellationToken.None).AsTask());
        ex.Message.ShouldBe("Param and Ct throw");
    }
}
