using Centra.Locks;
using Centra.Sample.MultiInstance.Domain;

namespace Centra.Sample.MultiInstance.Services;

public interface IClusterNodeLocalState
{
    string InstanceId { get; }
    string AppId { get; }
    bool IsLeader { get; }
    DateTimeOffset StartedAtUtc { get; }
    IReadOnlyList<TaskExecutionRecord> ProcessedTasks { get; }

    void SetLeaderLock(IDistributedLock? @lock);
    IDistributedLock? GetLeaderLock();
    void RecordTask(TaskExecutionRecord record);
    ClusterNodeInfo GetNodeInfo(string? hostAddress = null);
}
