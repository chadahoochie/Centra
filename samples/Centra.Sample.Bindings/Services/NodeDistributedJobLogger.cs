using Centra.Bindings;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.Bindings.Services;

/// <summary>
/// Node-aware logger decorator for <see cref="DistributedJobHandler"/>.
/// Intercepts lock collision skip logs from Centra runtime, records the skip in <see cref="ClusterExecutionTracker"/>,
/// and outputs formatted console indicators to clearly visualize cluster coordination.
/// </summary>
public sealed class NodeDistributedJobLogger : ILogger<DistributedJobHandler>
{
    private readonly INodeContext _nodeContext;
    private readonly ClusterExecutionTracker _tracker;

    public NodeDistributedJobLogger(INodeContext nodeContext, ClusterExecutionTracker tracker)
    {
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (message.Contains("skipped on this instance because another cluster replica acquired the lock", StringComparison.OrdinalIgnoreCase))
        {
            _tracker.RecordSkip(_nodeContext.NodeId);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   ⏭️  [{_nodeContext.NodeId}] SKIPPED (lock already acquired by peer node)");
            Console.ResetColor();
        }
    }
}
