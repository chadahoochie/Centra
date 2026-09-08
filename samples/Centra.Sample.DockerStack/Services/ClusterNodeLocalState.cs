using System.Collections.Concurrent;
using Centra.Hosting.Options;
using Centra.Locks;
using Centra.Sample.DockerStack.Domain;
using Microsoft.Extensions.Options;

namespace Centra.Sample.DockerStack.Services;

public sealed class ClusterNodeLocalState : IClusterNodeLocalState
{
    private readonly ConcurrentBag<TaskExecutionRecord> _processedTasks = new();
    private IDistributedLock? _currentLeaderLock;
    private readonly object _lockObj = new();

    public ClusterNodeLocalState(IOptions<CentraOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AppId = options.Value.AppId;
        InstanceId = options.Value.ControlPlane.InstanceId;
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public string InstanceId { get; }
    public string AppId { get; }
    public bool IsLeader => _currentLeaderLock is not null;
    public DateTimeOffset StartedAtUtc { get; }
    public IReadOnlyList<TaskExecutionRecord> ProcessedTasks => _processedTasks.ToArray();

    public void SetLeaderLock(IDistributedLock? @lock)
    {
        lock (_lockObj)
        {
            _currentLeaderLock = @lock;
        }
    }

    public IDistributedLock? GetLeaderLock()
    {
        lock (_lockObj)
        {
            return _currentLeaderLock;
        }
    }

    public void RecordTask(TaskExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _processedTasks.Add(record);
    }

    public ClusterNodeInfo GetNodeInfo(string? hostAddress = null)
    {
        return new ClusterNodeInfo(
            InstanceId,
            AppId,
            IsLeader,
            _processedTasks.Count,
            StartedAtUtc,
            hostAddress);
    }
}
