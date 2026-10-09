using System.Text;
using Centra.ControlPlane.Workflows;
using Centra.Core.Workflows;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Workflows;

public sealed class WorkflowHistoryRedactionTests
{
    [Fact]
    public void Redact_Should_Scrub_All_Payload_Data_And_Details()
    {
        var phiData = Encoding.UTF8.GetBytes("Patient SSN: 123-45-6789, Diagnosis: Cancer");
        var events = new List<WorkflowHistoryEventRecord>
        {
            new()
            {
                EventId = 1,
                EventType = 1,
                Name = "ExecuteMedicalBillingActivity",
                Timestamp = DateTimeOffset.UtcNow,
                Data = phiData,
                Details = "Patient Name: John Doe"
            }
        };

        var redacted = WorkflowHistoryRedactor.Redact(events);

        redacted.Count.ShouldBe(1);
        redacted[0].EventId.ShouldBe(1);
        redacted[0].Name.ShouldBe("ExecuteMedicalBillingActivity");
        redacted[0].Data.ShouldBeNull();
        redacted[0].Details.ShouldBe("[REDACTED]");
    }
}
