using Centra.Core.Workflows;
using Centra.Workflows;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowActivityMethodInvokerTests
{
    private readonly WorkflowActivityContext _context = new(
        new WorkflowInstanceId("wf-test"),
        "ActivityA");

    public sealed class ValueTaskVoidActivity
    {
        public bool Executed { get; private set; }
        public ValueTask RunAsync(WorkflowActivityContext ctx, string? input)
        {
            Executed = true;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class TaskVoidActivity
    {
        public bool Executed { get; private set; }
        public async Task RunAsync(WorkflowActivityContext ctx, string? input)
        {
            await Task.Yield();
            Executed = true;
        }
    }

    public sealed class ValueTaskGenericActivity
    {
        public ValueTask<int> RunAsync(WorkflowActivityContext ctx, int input)
        {
            return ValueTask.FromResult(input + 5);
        }
    }

    public sealed class TaskGenericActivity
    {
        public async Task<string> RunAsync(WorkflowActivityContext ctx, string input)
        {
            await Task.Yield();
            return $"Processed {input}";
        }
    }

    public sealed class SyncActivity
    {
        public bool RunAsync(WorkflowActivityContext ctx, bool input)
        {
            return !input;
        }
    }

    public sealed class FaultingActivity
    {
        public Task RunAsync(WorkflowActivityContext ctx, object? input)
        {
            throw new InvalidOperationException("Activity execution failed");
        }
    }

    public sealed class MissingRunAsyncActivity
    {
    }

    [Fact]
    public async Task InvokeActivityMethodAsync_Should_Execute_All_Return_Type_Variants()
    {
        var invoker = WorkflowActivityMethodInvoker.Instance;

        // 1. ValueTask void
        var act1 = new ValueTaskVoidActivity();
        var res1 = await invoker.InvokeActivityMethodAsync<object?>(act1, _context, "data", CancellationToken.None);
        res1.ShouldBeNull();
        act1.Executed.ShouldBeTrue();

        // 2. Task void
        var act2 = new TaskVoidActivity();
        var res2 = await invoker.InvokeActivityMethodAsync<object?>(act2, _context, "data", CancellationToken.None);
        res2.ShouldBeNull();
        act2.Executed.ShouldBeTrue();

        // 3. ValueTask<T>
        var act3 = new ValueTaskGenericActivity();
        var res3 = await invoker.InvokeActivityMethodAsync<int>(act3, _context, 10, CancellationToken.None);
        res3.ShouldBe(15);

        // 4. Task<T>
        var act4 = new TaskGenericActivity();
        var res4 = await invoker.InvokeActivityMethodAsync<string>(act4, _context, "invoice", CancellationToken.None);
        res4.ShouldBe("Processed invoice");

        // 5. Synchronous
        var act5 = new SyncActivity();
        var res5 = await invoker.InvokeActivityMethodAsync<bool>(act5, _context, true, CancellationToken.None);
        res5.ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeActivityMethodAsync_Should_Unwrap_TargetInvocationException()
    {
        var invoker = WorkflowActivityMethodInvoker.Instance;
        var act = new FaultingActivity();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            invoker.InvokeActivityMethodAsync<object?>(act, _context, null, CancellationToken.None).AsTask());

        ex.Message.ShouldBe("Activity execution failed");
    }

    [Fact]
    public void CreateInvoker_Should_Throw_When_RunAsync_Missing()
    {
        var invoker = WorkflowActivityMethodInvoker.Instance;

        Should.Throw<InvalidOperationException>(() =>
            invoker.CreateInvoker(typeof(MissingRunAsyncActivity)));
    }

    [Fact]
    public async Task InvokeActivityMethodAsync_Should_Validate_ActivityInstance_Not_Null()
    {
        var invoker = WorkflowActivityMethodInvoker.Instance;

        await Should.ThrowAsync<ArgumentNullException>(() =>
            invoker.InvokeActivityMethodAsync<object?>(null!, _context, null, CancellationToken.None).AsTask());
    }
}
