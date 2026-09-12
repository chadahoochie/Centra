using Centra.Drivers;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Bindings;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.InMemory.Locks;
using Centra.Providers.InMemory.PubSub;
using Centra.Providers.InMemory.State;
using Centra.Registry;
using Centra.Sample.Workflows.Domain;
using Centra.Sample.Workflows.Simulation;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Workflows;

public sealed class WorkflowClusterIntegrationTests
{
    [Fact]
    public async Task Should_Execute_Workflow_Simulation_Successfully()
    {
        // Act
        var result = await WorkflowDemoRunner.RunAsync();

        // Assert
        result.ShouldNotBeNull();
        result.HappyPathCompleted.ShouldBeTrue();
        result.SagaRollbackSucceeded.ShouldBeTrue();
        result.ExternalApprovalSucceeded.ShouldBeTrue();
        result.HappyPathOrderId.ShouldBe("ord-1001");
        result.SagaOrderId.ShouldBe("ord-1002");
        result.ApprovalRequestId.ShouldBe("req-approv-801");
        result.Logs.Count.ShouldBeGreaterThan(10);
    }

    [Fact]
    public async Task Should_Resume_Workflow_On_Different_Node_Using_Shared_State_Store()
    {
        // Arrange: Shared State Store Driver simulating cluster backend
        var sharedStateDriver = new InMemoryStateStoreDriver();
        var sharedLockDriver = new InMemoryDistributedLockDriver();

        var host1 = CreateWorkflowHost("node-alpha", sharedStateDriver, sharedLockDriver);
        var host2 = CreateWorkflowHost("node-beta", sharedStateDriver, sharedLockDriver);

        await host1.StartAsync();

        var instanceId = new WorkflowInstanceId("wf-cluster-cross-node-1");
        var request = new ApprovalRequest("req-100", "Lead Engineer", 3500m, "Datacenter Expansion");

        try
        {
            var client1 = host1.Services.GetRequiredService<IWorkflowClient>();

            // Act 1: Node 1 initiates workflow, which suspends on external event
            await client1.StartWorkflowAsync<ManagerApprovalWorkflow, ApprovalRequest>(
                request,
                instanceId.Value);

            var stateOnNode1 = await client1.GetWorkflowStateAsync(instanceId);
            stateOnNode1.ShouldNotBeNull();
            stateOnNode1.Value.Status.ShouldBe(WorkflowStatus.Suspended);

            // Act 2: Simulate Node 1 graceful crash / shutdown
            await host1.StopAsync();

            // Act 3: Node 2 starts up, connects to shared backend, resumes workflow turn
            await host2.StartAsync();
            var client2 = host2.Services.GetRequiredService<IWorkflowClient>();

            var stateOnNode2 = await client2.GetWorkflowStateAsync(instanceId);
            stateOnNode2.ShouldNotBeNull();
            stateOnNode2.Value.Status.ShouldBe(WorkflowStatus.Suspended);

            // Act 4: Raise external event on Node 2
            var decision = new ApprovalResponse("vp-eng", true, "Cross-node resume verified");
            await client2.RaiseEventAsync(instanceId, ManagerApprovalWorkflow.DecisionEventName, decision);

            var completionResult = await client2.WaitForWorkflowCompletionAsync<ApprovalResponse>(
                instanceId,
                TimeSpan.FromSeconds(10));

            // Assert: Workflow resumed and completed on Node 2 with deterministic replay
            completionResult.ShouldNotBeNull();
            completionResult.Approved.ShouldBeTrue();
            completionResult.ApproverId.ShouldBe("vp-eng");

            var finalState = await client2.GetWorkflowStateAsync(instanceId);
            finalState.ShouldNotBeNull();
            finalState.Value.Status.ShouldBe(WorkflowStatus.Completed);
        }
        finally
        {
            await host2.StopAsync();
            host1.Dispose();
            host2.Dispose();
        }
    }

    [Fact]
    public async Task Should_Rollback_Saga_Compensations_When_Activity_Fails()
    {
        // Arrange
        ReleaseInventoryCompensationActivity.CompensationExecutions = 0;
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "saga-integration-service";
                    options.DefaultStateStore = "statestore";
                    options.DefaultLockStore = "lockstore";
                });
                services.AddCentraInMemory();
                services.AddCentraWorkflows(options =>
                {
                    options.DefaultStateStore = "statestore";
                    options.DefaultLockStore = "lockstore";
                });

                services.AddWorkflow<OrderProcessingWorkflow>();
                services.AddWorkflowActivity<ValidateOrderActivity>();
                services.AddWorkflowActivity<ReserveInventoryActivity>();
                services.AddWorkflowActivity<ReleaseInventoryCompensationActivity>();
                services.AddWorkflowActivity<ProcessPaymentActivity>();
                services.AddWorkflowActivity<ShipOrderActivity>();
            })
            .Build();

        await host.StartAsync();

        try
        {
            var client = host.Services.GetRequiredService<IWorkflowClient>();
            var instanceId = new WorkflowInstanceId("wf-saga-fail-1");
            var request = new OrderProcessingRequest(
                "ord-fail-1",
                "Charlie",
                "SKU-HIGH-PRICE",
                1,
                5000.00m); // Exceeds $1000 threshold

            // Act
            await client.StartWorkflowAsync<OrderProcessingWorkflow, OrderProcessingRequest>(
                request,
                instanceId.Value);

            var result = await client.WaitForWorkflowCompletionAsync<OrderProcessingResult>(
                instanceId,
                TimeSpan.FromSeconds(10));

            // Assert
            result.ShouldNotBeNull();
            result.Status.ShouldBe("Failed");
            result.Notes.ShouldNotBeNull();
            result.Notes.ShouldContain("Saga compensated after error");
            ReleaseInventoryCompensationActivity.CompensationExecutions.ShouldBe(1);

            var history = await client.GetWorkflowHistoryAsync(instanceId);
            history.ShouldContain(e => e.EventType == WorkflowHistoryEventType.ActivityCompleted && e.Name == nameof(ReserveInventoryActivity));
            history.ShouldContain(e => e.EventType == WorkflowHistoryEventType.ActivityFailed && e.Name == nameof(ProcessPaymentActivity));
            history.ShouldContain(e => e.EventType == WorkflowHistoryEventType.ActivityCompleted && e.Name == nameof(ReleaseInventoryCompensationActivity));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    internal static IHost CreateWorkflowHost(
        string instanceId,
        InMemoryStateStoreDriver sharedStateDriver,
        InMemoryDistributedLockDriver sharedLockDriver)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "cluster-workflow-service";
                    options.DefaultStateStore = "shared-statestore";
                    options.DefaultLockStore = "shared-lockstore";
                    options.ControlPlane.InstanceId = instanceId;
                });

                services.AddCentraInMemory(
                    defaultStateStore: "shared-statestore",
                    defaultLockStore: "shared-lockstore");

                // Replace state store driver with shared instance
                services.AddSingleton(sharedStateDriver);
                services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<InMemoryStateStoreDriver>());

                services.AddSingleton(sharedLockDriver);
                services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<InMemoryDistributedLockDriver>());

                services.AddSingleton<IComponentInitializer>(sp => new InMemoryComponentInitializer(
                    sharedStateDriver,
                    sp.GetRequiredService<InMemoryPubSubDriver>(),
                    sharedLockDriver,
                    sp.GetRequiredService<InMemoryBindingDriver>(),
                    defaultStateStore: "shared-statestore",
                    defaultPubSub: "pubsub",
                    defaultLockStore: "shared-lockstore"));

                services.AddCentraWorkflows(options =>
                {
                    options.DefaultStateStore = "shared-statestore";
                    options.DefaultLockStore = "shared-lockstore";
                });

                services.AddWorkflow<ManagerApprovalWorkflow>();
            })
            .Build();
    }
}
