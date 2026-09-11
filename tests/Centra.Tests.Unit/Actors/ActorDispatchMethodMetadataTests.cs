using System.Reflection;
using Centra.Core.Actors;
using Centra.Invocation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorDispatchMethodMetadataTests
{
    private interface ITestActorInterface
    {
        Task TaskVoidMethodAsync();
        ValueTask ValueTaskVoidMethodAsync();
        Task<string> TaskGenericMethodAsync(int id);
        ValueTask<int> ValueTaskGenericMethodAsync(string name, CancellationToken ct);
        int InvalidSyncMethod();
        void InvalidVoidMethod();
    }

    [Fact]
    public void Create_Should_Identify_CancellationTokenIndex_And_ReturnTypes()
    {
        var taskVoid = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.TaskVoidMethodAsync))!;
        var meta1 = ActorDispatchMethodMetadata.Create(taskVoid);
        meta1.ReturnType.ShouldBe(typeof(Task));
        meta1.CancellationTokenIndex.ShouldBe(-1);

        var vtVoid = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.ValueTaskVoidMethodAsync))!;
        var meta2 = ActorDispatchMethodMetadata.Create(vtVoid);
        meta2.ReturnType.ShouldBe(typeof(ValueTask));
        meta2.CancellationTokenIndex.ShouldBe(-1);

        var taskGeneric = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.TaskGenericMethodAsync))!;
        var meta3 = ActorDispatchMethodMetadata.Create(taskGeneric);
        meta3.ReturnType.ShouldBe(typeof(Task<string>));
        meta3.CancellationTokenIndex.ShouldBe(-1);

        var vtGeneric = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.ValueTaskGenericMethodAsync))!;
        var meta4 = ActorDispatchMethodMetadata.Create(vtGeneric);
        meta4.ReturnType.ShouldBe(typeof(ValueTask<int>));
        meta4.CancellationTokenIndex.ShouldBe(1);
    }

    [Fact]
    public void Create_Should_Throw_For_Unsupported_Return_Types()
    {
        var syncMethod = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.InvalidSyncMethod))!;
        Should.Throw<NotSupportedException>(() => ActorDispatchMethodMetadata.Create(syncMethod));

        var voidMethod = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.InvalidVoidMethod))!;
        Should.Throw<NotSupportedException>(() => ActorDispatchMethodMetadata.Create(voidMethod));
    }

    [Fact]
    public async Task RemoteInvoker_Should_Invoke_Correct_ServiceInvoker_Methods()
    {
        var invoker = Substitute.For<IServiceInvoker>();

        // 1. Task Void
        var taskVoid = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.TaskVoidMethodAsync))!;
        var meta1 = ActorDispatchMethodMetadata.Create(taskVoid);
        _ = invoker.InvokeMethodAsync<object, object?>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<Centra.Invocation.ServiceInvocationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<object?>(null));

        var res1 = meta1.RemoteInvoker(invoker, "node-1", "/test", null, CancellationToken.None);
        res1.ShouldNotBeNull();
        await (Task)res1;

        // 2. ValueTask Void
        var vtVoid = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.ValueTaskVoidMethodAsync))!;
        var meta2 = ActorDispatchMethodMetadata.Create(vtVoid);
        var res2 = meta2.RemoteInvoker(invoker, "node-1", "/test", null, CancellationToken.None);
        res2.ShouldNotBeNull();
        await (ValueTask)res2;

        // 3. Task<string>
        var taskGeneric = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.TaskGenericMethodAsync))!;
        var meta3 = ActorDispatchMethodMetadata.Create(taskGeneric);
        _ = invoker.InvokeMethodAsync<object, string>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<Centra.Invocation.ServiceInvocationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("hello"));

        var res3 = meta3.RemoteInvoker(invoker, "node-1", "/test", null, CancellationToken.None);
        res3.ShouldNotBeNull();
        var taskResult = await (Task<string>)res3;
        taskResult.ShouldBe("hello");

        // 4. ValueTask<int>
        var vtGeneric = typeof(ITestActorInterface).GetMethod(nameof(ITestActorInterface.ValueTaskGenericMethodAsync))!;
        var meta4 = ActorDispatchMethodMetadata.Create(vtGeneric);
        _ = invoker.InvokeMethodAsync<object, int>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<Centra.Invocation.ServiceInvocationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(123));

        var res4 = meta4.RemoteInvoker(invoker, "node-1", "/test", null, CancellationToken.None);
        res4.ShouldNotBeNull();
        var vtResult = await (ValueTask<int>)res4;
        vtResult.ShouldBe(123);
    }
}
