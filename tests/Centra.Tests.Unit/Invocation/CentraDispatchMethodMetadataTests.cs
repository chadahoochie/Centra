using System.Reflection;
using Centra.Invocation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Invocation;

public sealed class CentraDispatchMethodMetadataTests
{
    private readonly IServiceInvoker _invoker = Substitute.For<IServiceInvoker>();

    private interface ITestDispatchService
    {
        Task<string> GetTaskGeneric(int id, CancellationToken ct);
        ValueTask<int> GetValueTaskGeneric(string name, CancellationToken ct);
        Task SendTaskVoid(int id, CancellationToken ct);
        ValueTask SendValueTaskVoid(string payload, CancellationToken ct);

        [ServiceMethod("custom-action", "PUT")]
        Task<bool> CustomMethod(int req);

        string UnsupportedSyncMethod();
    }

    [Fact]
    public async Task Create_TaskGeneric_ConfiguresMetadata_And_InvokesCorrectly()
    {
        var method = typeof(ITestDispatchService).GetMethod(nameof(ITestDispatchService.GetTaskGeneric))!;
        var metadata = CentraDispatchMethodMetadata.Create(method);

        metadata.MethodName.ShouldBe(nameof(ITestDispatchService.GetTaskGeneric));
        metadata.HttpVerb.ShouldBe("POST");
        metadata.BodyParameterIndex.ShouldBe(0);
        metadata.CancellationTokenIndex.ShouldBe(1);

        _invoker.InvokeMethodAsync<int, string>("app-1", nameof(ITestDispatchService.GetTaskGeneric), 42, "POST", null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<string>("task-result"));

        var resultObj = metadata.Invoker(_invoker, "app-1", metadata.MethodName, metadata.HttpVerb, 42, CancellationToken.None);
        resultObj.ShouldNotBeNull();
        var task = (Task<string>)resultObj;
        var result = await task;
        result.ShouldBe("task-result");
    }

    [Fact]
    public async Task Create_ValueTaskGeneric_ConfiguresMetadata_And_InvokesCorrectly()
    {
        var method = typeof(ITestDispatchService).GetMethod(nameof(ITestDispatchService.GetValueTaskGeneric))!;
        var metadata = CentraDispatchMethodMetadata.Create(method);

        metadata.MethodName.ShouldBe(nameof(ITestDispatchService.GetValueTaskGeneric));
        metadata.HttpVerb.ShouldBe("POST");

        _invoker.InvokeMethodAsync<string, int>("app-1", nameof(ITestDispatchService.GetValueTaskGeneric), "foo", "POST", null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(123));

        var resultObj = metadata.Invoker(_invoker, "app-1", metadata.MethodName, metadata.HttpVerb, "foo", CancellationToken.None);
        resultObj.ShouldNotBeNull();
        var vt = (ValueTask<int>)resultObj;
        var result = await vt;
        result.ShouldBe(123);
    }

    [Fact]
    public async Task Create_TaskVoid_ConfiguresMetadata_And_InvokesCorrectly()
    {
        var method = typeof(ITestDispatchService).GetMethod(nameof(ITestDispatchService.SendTaskVoid))!;
        var metadata = CentraDispatchMethodMetadata.Create(method);

        _invoker.InvokeMethodAsync<int, object?>("app-1", nameof(ITestDispatchService.SendTaskVoid), 99, "POST", null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<object?>((object?)null));

        var resultObj = metadata.Invoker(_invoker, "app-1", metadata.MethodName, metadata.HttpVerb, 99, CancellationToken.None);
        resultObj.ShouldNotBeNull();
        var task = (Task)resultObj;
        await task;
    }

    [Fact]
    public async Task Create_ValueTaskVoid_ConfiguresMetadata_And_InvokesCorrectly()
    {
        var method = typeof(ITestDispatchService).GetMethod(nameof(ITestDispatchService.SendValueTaskVoid))!;
        var metadata = CentraDispatchMethodMetadata.Create(method);

        _invoker.InvokeMethodAsync<string, object?>("app-1", nameof(ITestDispatchService.SendValueTaskVoid), "data", "POST", null, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<object?>((object?)null));

        var resultObj = metadata.Invoker(_invoker, "app-1", metadata.MethodName, metadata.HttpVerb, "data", CancellationToken.None);
        resultObj.ShouldNotBeNull();
        var vt = (ValueTask)resultObj;
        await vt;
    }

    [Fact]
    public void Create_ServiceMethodAttribute_AppliesCustomNameAndVerb()
    {
        var method = typeof(ITestDispatchService).GetMethod(nameof(ITestDispatchService.CustomMethod))!;
        var metadata = CentraDispatchMethodMetadata.Create(method);

        metadata.MethodName.ShouldBe("custom-action");
        metadata.HttpVerb.ShouldBe("PUT");
        metadata.CancellationTokenIndex.ShouldBe(-1);
        metadata.BodyParameterIndex.ShouldBe(0);
    }

    [Fact]
    public void Create_UnsupportedReturnType_ThrowsNotSupportedException()
    {
        var method = typeof(ITestDispatchService).GetMethod(nameof(ITestDispatchService.UnsupportedSyncMethod))!;
        Should.Throw<NotSupportedException>(() =>
            CentraDispatchMethodMetadata.Create(method));
    }
}
