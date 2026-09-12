using System.Security.Cryptography;
using System.Text;
using Centra.Serialization;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Execution context for workflow orchestrators supporting deterministic replay, durable timers, and CloudEvents awaits.
/// </summary>
public sealed class DeterministicWorkflowContext : IWorkflowContext
{
    private readonly IWorkflowActivityDispatcher _dispatcher;
    private readonly ICentraSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationToken _cancellationToken;
    private int _replayIndex;
    private int _sequenceNumber;

    public WorkflowInstanceId InstanceId { get; }
    public string WorkflowName { get; }
    public DateTimeOffset CurrentUtcDateTime { get; private set; }
    public bool IsReplaying { get; private set; }
    public IReadOnlyList<WorkflowHistoryEventRecord> PastHistory { get; }
    public List<WorkflowHistoryEventRecord> NewEvents { get; } = [];
    public string? CustomStatus { get; private set; }
    public string? WaitingEventName { get; private set; }
    public DateTimeOffset? TimerDueTime { get; private set; }
    public bool IsSuspended { get; private set; }
    public IWorkflowSaga Saga { get; }

    public DeterministicWorkflowContext(
        WorkflowInstanceId instanceId,
        string workflowName,
        IReadOnlyList<WorkflowHistoryEventRecord> pastHistory,
        IWorkflowActivityDispatcher dispatcher,
        ICentraSerializer serializer,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        InstanceId = instanceId;
        WorkflowName = workflowName;
        PastHistory = pastHistory;
        _dispatcher = dispatcher;
        _serializer = serializer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cancellationToken = cancellationToken;

        IsReplaying = pastHistory.Count > 0;
        CurrentUtcDateTime = pastHistory.Count > 0 ? pastHistory[0].Timestamp : _timeProvider.GetUtcNow();
        Saga = new WorkflowSaga(
            instanceId,
            dispatcher,
            (eventType, name, data, details) =>
            {
                NewEvents.Add(new WorkflowHistoryEventRecord
                {
                    EventId = NextEventId(),
                    EventType = (int)eventType,
                    Name = name,
                    Timestamp = _timeProvider.GetUtcNow(),
                    Data = data,
                    Details = details
                });
            });
    }

    public Guid NewGuid()
    {
        _sequenceNumber++;
        Span<byte> buffer = stackalloc byte[128];
        if (System.Text.Unicode.Utf8.TryWrite(buffer, $"{InstanceId.Value}:{_sequenceNumber}", out var bytesWritten))
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(buffer[..bytesWritten], hash);
            return new Guid(hash[..16]);
        }

        var payload = Encoding.UTF8.GetBytes($"{InstanceId.Value}:{_sequenceNumber}");
        var heapHash = SHA256.HashData(payload);
        return new Guid(heapHash.AsSpan(0, 16));
    }

    public void SetCustomStatus(string? status)
    {
        CustomStatus = status;
    }

    public IWorkflowSaga CreateSaga() => Saga;

    public ValueTask<TResult> CallActivityAsync<TActivity, TInput, TResult>(TInput input, ActivityOptions? options = null)
        where TActivity : class, IWorkflowActivity<TInput, TResult>
    {
        var name = typeof(TActivity).Name;
        return CallActivityAsync<TResult>(name, input, options);
    }

    public async ValueTask<TResult> CallActivityAsync<TResult>(string activityName, object? input, ActivityOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);

        if (IsReplaying && TryReplayActivity<TResult>(activityName, out var replayedResult))
        {
            return replayedResult;
        }

        return await ExecuteLiveActivityAsync<TResult>(activityName, input, options).ConfigureAwait(false);
    }

    public ValueTask CreateTimerAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (IsReplaying && TryReplayTimer(duration))
        {
            return ValueTask.CompletedTask;
        }

        return ExecuteLiveTimer(duration);
    }

    public ValueTask<TEvent> WaitForExternalEventAsync<TEvent>(
        string eventName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        if (IsReplaying && TryReplayExternalEvent<TEvent>(eventName, out var replayedEvent))
        {
            return replayedEvent;
        }

        return ExecuteLiveExternalEvent<TEvent>(eventName);
    }

    internal bool TryReplayActivity<TResult>(string activityName, out TResult result)
    {
        while (_replayIndex < PastHistory.Count)
        {
            var past = PastHistory[_replayIndex++];
            if (!string.Equals(past.Name, activityName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CurrentUtcDateTime = past.Timestamp;

            if (past.EventType == (int)WorkflowHistoryEventType.ActivityCompleted)
            {
                CheckReplayCompletion();
                result = past.Data is { Length: > 0 }
                    ? _serializer.Deserialize<TResult>(past.Data)!
                    : default!;
                return true;
            }

            if (past.EventType == (int)WorkflowHistoryEventType.ActivityFailed)
            {
                CheckReplayCompletion();
                throw new WorkflowActivityExecutionException(activityName, past.Details ?? "Activity previously failed.");
            }
        }

        CompleteReplay();
        result = default!;
        return false;
    }

    internal bool TryReplayTimer(TimeSpan duration)
    {
        while (_replayIndex < PastHistory.Count)
        {
            var past = PastHistory[_replayIndex++];
            if (past.EventType == (int)WorkflowHistoryEventType.TimerFired)
            {
                CurrentUtcDateTime = past.Timestamp;
                CheckReplayCompletion();
                return true;
            }

            if (past.EventType == (int)WorkflowHistoryEventType.TimerCreated)
            {
                if (HasFutureEvent(_replayIndex, WorkflowHistoryEventType.TimerFired))
                {
                    continue;
                }

                IsSuspended = true;
                TimerDueTime = DateTimeOffset.TryParse(past.Details, out var due)
                    ? due
                    : past.Timestamp + duration;

                throw new WorkflowSuspendedException($"Waiting for timer due at {TimerDueTime:O}");
            }
        }

        CompleteReplay();
        return false;
    }

    internal bool TryReplayExternalEvent<TEvent>(string eventName, out ValueTask<TEvent> result)
    {
        while (_replayIndex < PastHistory.Count)
        {
            var past = PastHistory[_replayIndex++];
            var matchesName = string.Equals(past.Name, eventName, StringComparison.OrdinalIgnoreCase);

            if (matchesName && past.EventType == (int)WorkflowHistoryEventType.ExternalEventReceived)
            {
                CurrentUtcDateTime = past.Timestamp;
                CheckReplayCompletion();
                var data = past.Data is { Length: > 0 }
                    ? _serializer.Deserialize<TEvent>(past.Data)!
                    : default!;
                result = new ValueTask<TEvent>(data);
                return true;
            }

            if (matchesName && past.EventType == (int)WorkflowHistoryEventType.ExternalEventAwaited)
            {
                if (HasFutureEvent(_replayIndex, WorkflowHistoryEventType.ExternalEventReceived, eventName))
                {
                    continue;
                }

                IsSuspended = true;
                WaitingEventName = eventName;
                throw new WorkflowSuspendedException($"Waiting for external event '{eventName}'");
            }
        }

        CompleteReplay();
        result = default;
        return false;
    }

    internal async ValueTask<TResult> ExecuteLiveActivityAsync<TResult>(
        string activityName,
        object? input,
        ActivityOptions? options)
    {
        var scheduledEvent = new WorkflowHistoryEventRecord
        {
            EventId = NextEventId(),
            EventType = (int)WorkflowHistoryEventType.ActivityScheduled,
            Name = activityName,
            Timestamp = CurrentUtcDateTime
        };
        NewEvents.Add(scheduledEvent);

        try
        {
            var result = await _dispatcher.DispatchActivityAsync<TResult>(
                InstanceId,
                activityName,
                input,
                options,
                _cancellationToken).ConfigureAwait(false);

            byte[]? data = result is not null ? _serializer.Serialize(result) : null;

            var completedEvent = new WorkflowHistoryEventRecord
            {
                EventId = NextEventId(),
                EventType = (int)WorkflowHistoryEventType.ActivityCompleted,
                Name = activityName,
                Timestamp = _timeProvider.GetUtcNow(),
                Data = data
            };
            NewEvents.Add(completedEvent);
            CurrentUtcDateTime = completedEvent.Timestamp;

            return result;
        }
        catch (Exception ex)
        {
            var failedEvent = new WorkflowHistoryEventRecord
            {
                EventId = NextEventId(),
                EventType = (int)WorkflowHistoryEventType.ActivityFailed,
                Name = activityName,
                Timestamp = _timeProvider.GetUtcNow(),
                Details = ex.Message
            };
            NewEvents.Add(failedEvent);
            CurrentUtcDateTime = failedEvent.Timestamp;
            throw;
        }
    }

    internal ValueTask ExecuteLiveTimer(TimeSpan duration)
    {
        var dueTime = CurrentUtcDateTime.Add(duration);
        var createdEvent = new WorkflowHistoryEventRecord
        {
            EventId = NextEventId(),
            EventType = (int)WorkflowHistoryEventType.TimerCreated,
            Name = "Timer",
            Timestamp = CurrentUtcDateTime,
            Details = dueTime.ToString("O")
        };
        NewEvents.Add(createdEvent);

        IsSuspended = true;
        TimerDueTime = dueTime;
        throw new WorkflowSuspendedException($"Waiting for timer due at {dueTime:O}");
    }

    internal ValueTask<TEvent> ExecuteLiveExternalEvent<TEvent>(string eventName)
    {
        var awaitedEvent = new WorkflowHistoryEventRecord
        {
            EventId = NextEventId(),
            EventType = (int)WorkflowHistoryEventType.ExternalEventAwaited,
            Name = eventName,
            Timestamp = CurrentUtcDateTime
        };
        NewEvents.Add(awaitedEvent);

        IsSuspended = true;
        WaitingEventName = eventName;
        throw new WorkflowSuspendedException($"Waiting for external event '{eventName}'");
    }

    internal bool HasFutureEvent(int fromIndex, WorkflowHistoryEventType eventType, string? eventName = null)
    {
        for (var j = fromIndex; j < PastHistory.Count; j++)
        {
            var ev = PastHistory[j];
            if (ev.EventType == (int)eventType &&
                (eventName is null || string.Equals(ev.Name, eventName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    internal void CompleteReplay()
    {
        IsReplaying = false;
        CurrentUtcDateTime = _timeProvider.GetUtcNow();
    }

    internal void CheckReplayCompletion()
    {
        if (_replayIndex >= PastHistory.Count)
        {
            IsReplaying = false;
            CurrentUtcDateTime = _timeProvider.GetUtcNow();
        }
    }

    internal long NextEventId()
    {
        long baseId = PastHistory.Count > 0 ? PastHistory[^1].EventId : 0;
        return baseId + NewEvents.Count + 1;
    }
}
