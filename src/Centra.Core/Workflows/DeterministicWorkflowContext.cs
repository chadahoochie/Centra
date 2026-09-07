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
        var payload = Encoding.UTF8.GetBytes($"{InstanceId.Value}:{_sequenceNumber}");
        var hash = SHA256.HashData(payload);
        return new Guid(hash.AsSpan(0, 16));
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

        if (IsReplaying)
        {
            // Scan for completed or failed activity in past history
            while (_replayIndex < PastHistory.Count)
            {
                var past = PastHistory[_replayIndex++];
                if (string.Equals(past.Name, activityName, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentUtcDateTime = past.Timestamp;

                    if (past.EventType == (int)WorkflowHistoryEventType.ActivityCompleted)
                    {
                        CheckReplayCompletion();
                        if (past.Data is not null && past.Data.Length > 0)
                        {
                            return _serializer.Deserialize<TResult>(past.Data)!;
                        }
                        return default!;
                    }

                    if (past.EventType == (int)WorkflowHistoryEventType.ActivityFailed)
                    {
                        CheckReplayCompletion();
                        throw new WorkflowActivityExecutionException(activityName, past.Details ?? "Activity previously failed.");
                    }
                }
            }

            // Exhausted past history
            IsReplaying = false;
            CurrentUtcDateTime = _timeProvider.GetUtcNow();
        }

        // Live execution
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

            byte[]? data = null;
            if (result is not null)
            {
                data = _serializer.Serialize(result);
            }

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

    public ValueTask CreateTimerAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (IsReplaying)
        {
            while (_replayIndex < PastHistory.Count)
            {
                var past = PastHistory[_replayIndex++];
                if (past.EventType == (int)WorkflowHistoryEventType.TimerFired)
                {
                    CurrentUtcDateTime = past.Timestamp;
                    CheckReplayCompletion();
                    return ValueTask.CompletedTask;
                }

                if (past.EventType == (int)WorkflowHistoryEventType.TimerCreated)
                {
                    // Check if TimerFired follows later in past history
                    bool firedFound = false;
                    for (int j = _replayIndex; j < PastHistory.Count; j++)
                    {
                        if (PastHistory[j].EventType == (int)WorkflowHistoryEventType.TimerFired)
                        {
                            firedFound = true;
                            break;
                        }
                    }

                    if (!firedFound)
                    {
                        IsSuspended = true;
                        if (DateTimeOffset.TryParse(past.Details, out var due))
                        {
                            TimerDueTime = due;
                        }
                        else
                        {
                            TimerDueTime = past.Timestamp + duration;
                        }
                        throw new WorkflowSuspendedException($"Waiting for timer due at {TimerDueTime:O}");
                    }
                }
            }

            IsReplaying = false;
            CurrentUtcDateTime = _timeProvider.GetUtcNow();
        }

        // Live timer creation
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

    public ValueTask<TEvent> WaitForExternalEventAsync<TEvent>(
        string eventName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        if (IsReplaying)
        {
            while (_replayIndex < PastHistory.Count)
            {
                var past = PastHistory[_replayIndex++];
                if (past.EventType == (int)WorkflowHistoryEventType.ExternalEventReceived &&
                    string.Equals(past.Name, eventName, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentUtcDateTime = past.Timestamp;
                    CheckReplayCompletion();
                    if (past.Data is not null && past.Data.Length > 0)
                    {
                        return new ValueTask<TEvent>(_serializer.Deserialize<TEvent>(past.Data)!);
                    }
                    return new ValueTask<TEvent>(default(TEvent)!);
                }

                if (past.EventType == (int)WorkflowHistoryEventType.ExternalEventAwaited &&
                    string.Equals(past.Name, eventName, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if event was received later in past history
                    bool received = false;
                    for (int j = _replayIndex; j < PastHistory.Count; j++)
                    {
                        if (PastHistory[j].EventType == (int)WorkflowHistoryEventType.ExternalEventReceived &&
                            string.Equals(PastHistory[j].Name, eventName, StringComparison.OrdinalIgnoreCase))
                        {
                            received = true;
                            break;
                        }
                    }

                    if (!received)
                    {
                        IsSuspended = true;
                        WaitingEventName = eventName;
                        throw new WorkflowSuspendedException($"Waiting for external event '{eventName}'");
                    }
                }
            }

            IsReplaying = false;
            CurrentUtcDateTime = _timeProvider.GetUtcNow();
        }

        // Live await
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

    private void CheckReplayCompletion()
    {
        if (_replayIndex >= PastHistory.Count)
        {
            IsReplaying = false;
            CurrentUtcDateTime = _timeProvider.GetUtcNow();
        }
    }

    private long NextEventId()
    {
        long baseId = PastHistory.Count > 0 ? PastHistory[^1].EventId : 0;
        return baseId + NewEvents.Count + 1;
    }
}
