using System.Threading.Tasks;
using Centra.Sample.ControlPlane.Simulation;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.ControlPlane;

public sealed class ControlPlaneSimulationIntegrationTests
{
    [Fact]
    public async Task ControlPlaneDemoRunner_Should_Execute_All_Six_Steps_Successfully()
    {
        // Act
        var result = await ControlPlaneDemoRunner.RunAsync();

        // Assert
        result.ShouldNotBeNull();
        result.HaRoutingSuccess.ShouldBeTrue();
        result.RogueNodeDefenseSuccess.ShouldBeTrue();
        result.MultiClusterExpansionSuccess.ShouldBeTrue();
        result.DynamicSyncSuccess.ShouldBeTrue();
        result.HaFailoverSuccess.ShouldBeTrue();
        result.DashboardVerificationSuccess.ShouldBeTrue();
        result.AllStepsSucceeded.ShouldBeTrue();
        result.SummaryNotes.Count.ShouldBe(6);
    }
}
