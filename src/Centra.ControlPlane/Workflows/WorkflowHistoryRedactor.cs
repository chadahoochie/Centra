using Centra.Core.Workflows;

namespace Centra.ControlPlane.Workflows;

public static class WorkflowHistoryRedactor
{
    public static List<WorkflowHistoryEventRecord> Redact(List<WorkflowHistoryEventRecord> events)
    {
        var redacted = new List<WorkflowHistoryEventRecord>(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            redacted.Add(new WorkflowHistoryEventRecord
            {
                EventId = e.EventId,
                EventType = e.EventType,
                Name = e.Name,
                Timestamp = e.Timestamp,
                Data = null,
                Details = "[REDACTED]"
            });
        }

        return redacted;
    }
}
