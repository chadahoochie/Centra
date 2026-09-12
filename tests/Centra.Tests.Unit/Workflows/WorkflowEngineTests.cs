using Centra.Core.Workflows;
using Centra.Serialization;
using Centra.State;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowEngineTests
{
    private readonly IStateStore _stateStore;
    private readonly ICentraSerializer _serializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ServiceCollection _services;

    public WorkflowEngineTests()
    {
        _stateStore = Substitute.For<IStateStore>();
        _serializer = new JsonCentraSerializer();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        _services = new ServiceCollection();
    }

    internal (IWorkflowEngine Engine, IWorkflowRegistry Registry, IWorkflowHistoryStore HistoryStore) CreateEngine(
        Dictionary<string, object> stateDatabase)
    {
        // Mock state store backed by stateDatabase
        _stateStore.GetAsync<WorkflowStateRecord>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                if (stateDatabase.TryGetValue(key, out var val) && val is WorkflowStateRecord record)
                {
                    return new StateEntry<WorkflowStateRecord>(key, record, "etag-1");
                }
                return null;
            });

        _stateStore.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkflowStateRecord>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                var record = callInfo.Arg<WorkflowStateRecord>();
                stateDatabase[key] = record;
                return ValueTask.CompletedTask;
            });

        _stateStore.GetAsync<WorkflowHistoryRecord>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                if (stateDatabase.TryGetValue(key, out var val) && val is WorkflowHistoryRecord record)
                {
                    return new StateEntry<WorkflowHistoryRecord>(key, record, "etag-hist-1");
                }
                return null;
            });

        _stateStore.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkflowHistoryRecord>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                var record = callInfo.Arg<WorkflowHistoryRecord>();
                stateDatabase[key] = record;
                return ValueTask.CompletedTask;
            });

        var historyStore = new WorkflowHistoryStore(_stateStore, "statestore");
        var registry = new WorkflowRegistry();
        var sp = _services.BuildServiceProvider();
        var dispatcher = new WorkflowActivityDispatcher(sp, registry, _serializer);
        var engine = new WorkflowEngine(sp, registry, historyStore, dispatcher, _serializer, _timeProvider);

        return (engine, registry, historyStore);
    }

    [Fact]
    public async Task Should_Execute_Sequential_Activities_To_Completion()
    {
        // Arrange
        var db = new Dictionary<string, object>();
        var (engine, registry, _) = CreateEngine(db);

        registry.RegisterWorkflow(new WorkflowDefinition(
            nameof(SimpleOrderWorkflow),
            typeof(SimpleOrderWorkflow),
            typeof(OrderRequest),
            typeof(OrderResult)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(ValidateOrderActivity),
            typeof(ValidateOrderActivity),
            typeof(OrderRequest),
            typeof(bool)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(ProcessPaymentActivity),
            typeof(ProcessPaymentActivity),
            typeof(decimal),
            typeof(string)));

        // Act
        var request = new OrderRequest("prod-1", 100m);
        var instanceId = await engine.StartWorkflowAsync(nameof(SimpleOrderWorkflow), request);

        // Assert
        var state = await engine.GetWorkflowStateAsync(instanceId);
        state.ShouldNotBeNull();
        state.Value.Status.ShouldBe(WorkflowStatus.Completed);
        state.Value.CustomStatus.ShouldBe("OrderFulfilled");

        var result = await engine.WaitForWorkflowCompletionAsync<OrderResult>(instanceId);
        result.OrderId.ShouldBe("order-validated-prod-1");
        result.TransactionId.ShouldBe("txn-100");

        var history = await engine.GetWorkflowHistoryAsync(instanceId);
        history.Count.ShouldBeGreaterThan(3);
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.WorkflowStarted);
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.ActivityCompleted && h.Name == nameof(ValidateOrderActivity));
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.ActivityCompleted && h.Name == nameof(ProcessPaymentActivity));
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.WorkflowCompleted);
    }

    [Fact]
    public async Task Should_Execute_Saga_Compensations_In_Reverse_Order_When_Step_Fails()
    {
        // Arrange
        var db = new Dictionary<string, object>();
        var (engine, registry, _) = CreateEngine(db);

        registry.RegisterWorkflow(new WorkflowDefinition(
            nameof(SagaOrderWorkflow),
            typeof(SagaOrderWorkflow),
            typeof(OrderRequest),
            typeof(OrderResult)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(ReserveStockActivity),
            typeof(ReserveStockActivity),
            typeof(string),
            typeof(bool)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(ReleaseStockCompensationActivity),
            typeof(ReleaseStockCompensationActivity),
            typeof(string),
            typeof(bool)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(FailingPaymentActivity),
            typeof(FailingPaymentActivity),
            typeof(decimal),
            typeof(string)));

        ReleaseStockCompensationActivity.Compensated = false;

        // Act
        var request = new OrderRequest("prod-999", 500m);
        var instanceId = await engine.StartWorkflowAsync(nameof(SagaOrderWorkflow), request);

        // Assert
        var state = await engine.GetWorkflowStateAsync(instanceId);
        state.ShouldNotBeNull();
        state.Value.Status.ShouldBe(WorkflowStatus.Failed);
        state.Value.FailureDetails.ShouldNotBeNull();
        state.Value.FailureDetails!.ShouldContain("Payment gateway declined");

        ReleaseStockCompensationActivity.Compensated.ShouldBeTrue();

        var history = await engine.GetWorkflowHistoryAsync(instanceId);
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.SagaCompensationStarted);
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.SagaCompensationCompleted);
        history.ShouldContain(h => h.EventType == (int)WorkflowHistoryEventType.WorkflowFailed);
    }

    [Fact]
    public async Task Should_Suspend_On_Durable_Timer_And_Resume_When_Fired()
    {
        // Arrange
        var db = new Dictionary<string, object>();
        var (engine, registry, _) = CreateEngine(db);

        registry.RegisterWorkflow(new WorkflowDefinition(
            nameof(TimerDelayedWorkflow),
            typeof(TimerDelayedWorkflow),
            typeof(string),
            typeof(string)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(StepOneActivity),
            typeof(StepOneActivity),
            typeof(string),
            typeof(string)));

        registry.RegisterActivity(new WorkflowActivityDefinition(
            nameof(StepTwoActivity),
            typeof(StepTwoActivity),
            typeof(string),
            typeof(string)));

        // Act 1: Start workflow
        var instanceId = await engine.StartWorkflowAsync(nameof(TimerDelayedWorkflow), "input-data");

        // Assert 1: Suspended waiting for timer
        var state = await engine.GetWorkflowStateAsync(instanceId);
        state.ShouldNotBeNull();
        state.Value.Status.ShouldBe(WorkflowStatus.Suspended);
        state.Value.CustomStatus.ShouldBe("WaitingForDelay");

        // Act 2: Advance time and fire timer
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        await engine.FireTimerAsync(instanceId);

        // Assert 2: Completed successfully
        var finalState = await engine.GetWorkflowStateAsync(instanceId);
        finalState.ShouldNotBeNull();
        finalState.Value.Status.ShouldBe(WorkflowStatus.Completed);
        finalState.Value.CustomStatus.ShouldBe("DelayFinished");

        var output = await engine.WaitForWorkflowCompletionAsync<string>(instanceId);
        output.ShouldBe("step2-step1-input-data");
    }

    [Fact]
    public async Task Should_Suspend_On_External_Event_And_Resume_When_Raised()
    {
        // Arrange
        var db = new Dictionary<string, object>();
        var (engine, registry, _) = CreateEngine(db);

        registry.RegisterWorkflow(new WorkflowDefinition(
            nameof(ApprovalWorkflow),
            typeof(ApprovalWorkflow),
            typeof(string),
            typeof(ApprovalOutcome)));

        // Act 1: Start workflow
        var instanceId = await engine.StartWorkflowAsync(nameof(ApprovalWorkflow), "PO-1234");

        // Assert 1: Suspended waiting for ApprovalEvent
        var state = await engine.GetWorkflowStateAsync(instanceId);
        state.ShouldNotBeNull();
        state.Value.Status.ShouldBe(WorkflowStatus.Suspended);
        state.Value.CustomStatus.ShouldBe("AwaitingApproval");

        // Act 2: Raise external approval event
        var approvalData = new ApprovalDecision("manager-bob", true);
        await engine.RaiseEventAsync(instanceId, "ApprovalEvent", approvalData);

        // Assert 2: Completed with approved outcome
        var finalState = await engine.GetWorkflowStateAsync(instanceId);
        finalState.ShouldNotBeNull();
        finalState.Value.Status.ShouldBe(WorkflowStatus.Completed);

        var outcome = await engine.WaitForWorkflowCompletionAsync<ApprovalOutcome>(instanceId);
        outcome.PoNumber.ShouldBe("PO-1234");
        outcome.ApprovedBy.ShouldBe("manager-bob");
        outcome.IsApproved.ShouldBeTrue();
    }

    [Fact]
    public void DeterministicWorkflowContext_NewGuid_Should_Be_Deterministic()
    {
        // Arrange
        var id = new WorkflowInstanceId("wf-deterministic-guid");
        var dispatcher = Substitute.For<IWorkflowActivityDispatcher>();
        var context1 = new DeterministicWorkflowContext(id, "Wf", [], dispatcher, _serializer, _timeProvider);
        var context2 = new DeterministicWorkflowContext(id, "Wf", [], dispatcher, _serializer, _timeProvider);

        // Act
        var guid1A = context1.NewGuid();
        var guid1B = context1.NewGuid();

        var guid2A = context2.NewGuid();
        var guid2B = context2.NewGuid();

        // Assert
        guid1A.ShouldBe(guid2A);
        guid1B.ShouldBe(guid2B);
        guid1A.ShouldNotBe(guid1B);
    }

    [Fact]
    public void Dispose_DisposesTimerScheduler()
    {
        var timerScheduler = Substitute.For<IWorkflowTimerScheduler>();
        var historyStore = Substitute.For<IWorkflowHistoryStore>();
        var dispatcher = Substitute.For<IWorkflowActivityDispatcher>();
        var registry = new WorkflowRegistry();
        var sp = _services.BuildServiceProvider();

        var engine = new WorkflowEngine(
            sp,
            registry,
            historyStore,
            dispatcher,
            _serializer,
            _timeProvider,
            null,
            null,
            timerScheduler,
            null);

        engine.Dispose();

        timerScheduler.Received(1).Dispose();
    }

    [Fact]
    public void Constructor_NullRequiredArguments_ThrowsArgumentNullException()
    {
        var historyStore = Substitute.For<IWorkflowHistoryStore>();
        var dispatcher = Substitute.For<IWorkflowActivityDispatcher>();
        var registry = new WorkflowRegistry();
        var sp = _services.BuildServiceProvider();

        Should.Throw<ArgumentNullException>(() =>
            new WorkflowEngine(null!, registry, historyStore, dispatcher, _serializer));

        Should.Throw<ArgumentNullException>(() =>
            new WorkflowEngine(sp, null!, historyStore, dispatcher, _serializer));

        Should.Throw<ArgumentNullException>(() =>
            new WorkflowEngine(sp, registry, null!, dispatcher, _serializer));

        Should.Throw<ArgumentNullException>(() =>
            new WorkflowEngine(sp, registry, historyStore, dispatcher, null!));
    }
}

// ======================= Test Workflows & Activities =======================

public sealed record OrderRequest(string ProductId, decimal Amount);
public sealed record OrderResult(string OrderId, string TransactionId);

public sealed class SimpleOrderWorkflow : Workflow<OrderRequest, OrderResult>
{
    public override async ValueTask<OrderResult> RunAsync(IWorkflowContext context, OrderRequest input)
    {
        var isValid = await context.CallActivityAsync<ValidateOrderActivity, OrderRequest, bool>(input);
        if (!isValid)
        {
            throw new InvalidOperationException("Order validation failed.");
        }

        var txnId = await context.CallActivityAsync<ProcessPaymentActivity, decimal, string>(input.Amount);
        context.SetCustomStatus("OrderFulfilled");

        return new OrderResult($"order-validated-{input.ProductId}", txnId);
    }
}

public sealed class ValidateOrderActivity : WorkflowActivity<OrderRequest, bool>
{
    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, OrderRequest input)
    {
        return ValueTask.FromResult(!string.IsNullOrWhiteSpace(input.ProductId));
    }
}

public sealed class ProcessPaymentActivity : WorkflowActivity<decimal, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, decimal input)
    {
        return ValueTask.FromResult($"txn-{input}");
    }
}

public sealed class SagaOrderWorkflow : Workflow<OrderRequest, OrderResult>
{
    public override async ValueTask<OrderResult> RunAsync(IWorkflowContext context, OrderRequest input)
    {
        var saga = context.CreateSaga();

        // Step 1: Reserve stock
        await context.CallActivityAsync<ReserveStockActivity, string, bool>(input.ProductId);

        // Register compensation in case subsequent steps fail!
        saga.AddCompensation(nameof(ReleaseStockCompensationActivity), input.ProductId);

        // Step 2: Payment fails
        await context.CallActivityAsync<FailingPaymentActivity, decimal, string>(input.Amount);

        return new OrderResult("never-reached", "none");
    }
}

public sealed class ReserveStockActivity : WorkflowActivity<string, bool>
{
    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, string input)
    {
        return ValueTask.FromResult(true);
    }
}

public sealed class ReleaseStockCompensationActivity : WorkflowActivity<string, bool>
{
    public static bool Compensated { get; set; }

    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, string input)
    {
        Compensated = true;
        return ValueTask.FromResult(true);
    }
}

public sealed class FailingPaymentActivity : WorkflowActivity<decimal, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, decimal input)
    {
        throw new InvalidOperationException("Payment gateway declined the transaction.");
    }
}

public sealed class TimerDelayedWorkflow : Workflow<string, string>
{
    public override async ValueTask<string> RunAsync(IWorkflowContext context, string input)
    {
        var step1 = await context.CallActivityAsync<StepOneActivity, string, string>(input);
        context.SetCustomStatus("WaitingForDelay");

        // Durable pause
        await context.CreateTimerAsync(TimeSpan.FromMinutes(5));

        context.SetCustomStatus("DelayFinished");
        var step2 = await context.CallActivityAsync<StepTwoActivity, string, string>(step1);
        return step2;
    }
}

public sealed class StepOneActivity : WorkflowActivity<string, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
    {
        return ValueTask.FromResult($"step1-{input}");
    }
}

public sealed class StepTwoActivity : WorkflowActivity<string, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
    {
        return ValueTask.FromResult($"step2-{input}");
    }
}

public sealed record ApprovalDecision(string ApprovedBy, bool IsApproved);
public sealed record ApprovalOutcome(string PoNumber, string ApprovedBy, bool IsApproved);

public sealed class ApprovalWorkflow : Workflow<string, ApprovalOutcome>
{
    public override async ValueTask<ApprovalOutcome> RunAsync(IWorkflowContext context, string input)
    {
        context.SetCustomStatus("AwaitingApproval");
        var decision = await context.WaitForExternalEventAsync<ApprovalDecision>("ApprovalEvent");
        return new ApprovalOutcome(input, decision.ApprovedBy, decision.IsApproved);
    }
}
