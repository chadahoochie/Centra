using Centra.Core.Workflows;
using Centra.Workflows;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowRunnerInvokerTests
{
    private readonly IWorkflowContext _context = Substitute.For<IWorkflowContext>();

    public sealed class ValueTaskVoidWorkflow
    {
        public bool Executed { get; private set; }
        public ValueTask RunAsync(IWorkflowContext ctx, string? input)
        {
            Executed = true;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class TaskVoidWorkflow
    {
        public bool Executed { get; private set; }
        public async Task RunAsync(IWorkflowContext ctx, string? input)
        {
            await Task.Yield();
            Executed = true;
        }
    }

    public sealed class ValueTaskGenericWorkflow
    {
        public ValueTask<int> RunAsync(IWorkflowContext ctx, int input)
        {
            return ValueTask.FromResult(input * 10);
        }
    }

    public sealed class TaskGenericWorkflow
    {
        public async Task<string> RunAsync(IWorkflowContext ctx, string input)
        {
            await Task.Yield();
            return $"Hello, {input}!";
        }
    }

    public sealed class SyncWorkflow
    {
        public double RunAsync(IWorkflowContext ctx, double input)
        {
            return input * 2.5;
        }
    }

    public sealed class FaultingWorkflow
    {
        public Task RunAsync(IWorkflowContext ctx, object? input)
        {
            throw new ApplicationException("Workflow boom");
        }
    }

    public sealed class MissingRunAsyncWorkflow
    {
    }

    [Fact]
    public async Task InvokeWorkflowRunAsync_Should_Execute_All_Return_Type_Variants()
    {
        var invoker = WorkflowRunnerInvoker.Instance;

        // 1. ValueTask void
        var wf1 = new ValueTaskVoidWorkflow();
        var res1 = await invoker.InvokeWorkflowRunAsync(wf1, _context, "test");
        res1.ShouldBeNull();
        wf1.Executed.ShouldBeTrue();

        // 2. Task void
        var wf2 = new TaskVoidWorkflow();
        var res2 = await invoker.InvokeWorkflowRunAsync(wf2, _context, "test");
        res2.ShouldBeNull();
        wf2.Executed.ShouldBeTrue();

        // 3. ValueTask<T>
        var wf3 = new ValueTaskGenericWorkflow();
        var res3 = await invoker.InvokeWorkflowRunAsync(wf3, _context, 7);
        res3.ShouldBe(70);

        // 4. Task<T>
        var wf4 = new TaskGenericWorkflow();
        var res4 = await invoker.InvokeWorkflowRunAsync(wf4, _context, "Centra");
        res4.ShouldBe("Hello, Centra!");

        // 5. Synchronous
        var wf5 = new SyncWorkflow();
        var res5 = await invoker.InvokeWorkflowRunAsync(wf5, _context, 4.0);
        res5.ShouldBe(10.0);
    }

    [Fact]
    public async Task InvokeWorkflowRunAsync_Should_Unwrap_TargetInvocationException()
    {
        var invoker = WorkflowRunnerInvoker.Instance;
        var wf = new FaultingWorkflow();

        var ex = await Should.ThrowAsync<ApplicationException>(() =>
            invoker.InvokeWorkflowRunAsync(wf, _context, null).AsTask());

        ex.Message.ShouldBe("Workflow boom");
    }

    [Fact]
    public void CreateWorkflowRunner_Should_Throw_When_RunAsync_Missing()
    {
        var invoker = WorkflowRunnerInvoker.Instance;

        Should.Throw<InvalidOperationException>(() =>
            invoker.CreateWorkflowRunner(typeof(MissingRunAsyncWorkflow)));
    }

    [Fact]
    public async Task InvokeWorkflowRunAsync_Should_Validate_WorkflowInstance_Not_Null()
    {
        var invoker = WorkflowRunnerInvoker.Instance;

        await Should.ThrowAsync<ArgumentNullException>(() =>
            invoker.InvokeWorkflowRunAsync(null!, _context, null).AsTask());
    }
}
