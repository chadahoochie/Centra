using Centra.Core.Workflows;
using Centra.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowSagaTests
{
    private readonly WorkflowInstanceId _instanceId = new("wf-saga-test");
    private readonly IWorkflowActivityDispatcher _dispatcher = Substitute.For<IWorkflowActivityDispatcher>();

    public sealed class DummyCancelActivity : IWorkflowActivity<string, bool>
    {
        public ValueTask<bool> RunAsync(WorkflowActivityContext context, string input) => ValueTask.FromResult(true);
    }

    [Fact]
    public async Task CompensateAsync_Should_Return_Immediately_When_Compensations_Empty()
    {
        var saga = new WorkflowSaga(_instanceId, _dispatcher);
        await saga.CompensateAsync(CancellationToken.None);

        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchActivityAsync<object>(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task CompensateAsync_Should_Execute_In_LIFO_Order()
    {
        var eventLog = new List<string>();
        var saga = new WorkflowSaga(
            _instanceId,
            _dispatcher,
            onEvent: (type, name, data, details) => eventLog.Add($"{type}:{name}"),
            logger: NullLogger.Instance);

        saga.AddCompensation("Step1", "input1");
        saga.AddCompensation("Step2", "input2");
        saga.AddCompensation<DummyCancelActivity, string>("input3");

        saga.Compensations.Count.ShouldBe(3);

        var executedSteps = new List<string>();
        _dispatcher.DispatchActivityAsync<object>(
            _instanceId,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<ActivityOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                executedSteps.Add(ci.Arg<string>());
                return ValueTask.FromResult<object>(true);
            });

        await saga.CompensateAsync(CancellationToken.None);

        executedSteps.ShouldBe(["DummyCancelActivity", "Step2", "Step1"]);
        eventLog.ShouldContain("SagaCompensationStarted:Saga");
        eventLog.ShouldContain("SagaCompensationCompleted:Saga");
    }

    [Fact]
    public async Task CompensateAsync_Should_Aggregate_Exceptions_When_Steps_Fail()
    {
        var saga = new WorkflowSaga(_instanceId, _dispatcher, logger: NullLogger.Instance);
        saga.AddCompensation("FaultyStep1", "in1");
        saga.AddCompensation("FaultyStep2", "in2");

        _dispatcher.DispatchActivityAsync<object>(
            _instanceId,
            "FaultyStep2",
            Arg.Any<object?>(),
            Arg.Any<ActivityOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns<ValueTask<object>>(_ => throw new InvalidOperationException("Step 2 failed"));

        _dispatcher.DispatchActivityAsync<object>(
            _instanceId,
            "FaultyStep1",
            Arg.Any<object?>(),
            Arg.Any<ActivityOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns<ValueTask<object>>(_ => throw new ApplicationException("Step 1 failed"));

        var aggEx = await Should.ThrowAsync<AggregateException>(() => saga.CompensateAsync(CancellationToken.None).AsTask());

        aggEx.InnerExceptions.Count.ShouldBe(2);
        aggEx.InnerExceptions.Any(e => e.Message == "Step 2 failed").ShouldBeTrue();
        aggEx.InnerExceptions.Any(e => e.Message == "Step 1 failed").ShouldBeTrue();
    }

    [Fact]
    public void AddCompensation_Should_Validate_ActivityName()
    {
        var saga = new WorkflowSaga(_instanceId, _dispatcher);
        Should.Throw<ArgumentException>(() => saga.AddCompensation("", null));
        Should.Throw<ArgumentException>(() => saga.AddCompensation("   ", null));
    }
}
