using Centra.Core.Workflows;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraWorkflowHostingTests
{
    [Fact]
    public void AddCentraWorkflows_Should_Register_All_Required_Services()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCentraCore(options => options.AppId = "test-app");
        services.AddCentraState();
        services.AddCentraInMemory();
        services.AddCentraWorkflows();
        services.AddCentraWorkflow<HostedSampleWorkflow>();
        services.AddCentraWorkflowActivity<HostedSampleActivity>();

        // Act
        using var sp = services.BuildServiceProvider();

        // Assert
        var registry = sp.GetService<IWorkflowRegistry>();
        registry.ShouldNotBeNull();
        registry.TryGetWorkflow(nameof(HostedSampleWorkflow), out var wfDef).ShouldBeTrue();
        wfDef.ShouldNotBeNull();
        wfDef.Name.ShouldBe(nameof(HostedSampleWorkflow));

        registry.TryGetActivity(nameof(HostedSampleActivity), out var actDef).ShouldBeTrue();
        actDef.ShouldNotBeNull();
        actDef.Name.ShouldBe(nameof(HostedSampleActivity));

        var engine = sp.GetService<IWorkflowEngine>();
        engine.ShouldNotBeNull();

        var client = sp.GetService<IWorkflowClient>();
        client.ShouldNotBeNull();

        var historyStore = sp.GetService<IWorkflowHistoryStore>();
        historyStore.ShouldNotBeNull();
    }
}

public sealed class HostedSampleWorkflow : Workflow<string, string>
{
    public override ValueTask<string> RunAsync(IWorkflowContext context, string input)
    {
        return ValueTask.FromResult($"hosted-{input}");
    }
}

public sealed class HostedSampleActivity : WorkflowActivity<string, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
    {
        return ValueTask.FromResult($"act-{input}");
    }
}
