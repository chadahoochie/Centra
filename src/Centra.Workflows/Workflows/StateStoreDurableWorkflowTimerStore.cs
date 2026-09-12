using Centra.State;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Persistent implementation of <see cref="IDurableWorkflowTimerStore"/> backed by an <see cref="IStateStore"/>.
/// </summary>
public sealed class StateStoreDurableWorkflowTimerStore : IDurableWorkflowTimerStore
{
    private const string ActiveIndexKey = "centra:workflows:timers:active";
    private readonly IStateStore _stateStore;
    private readonly string _storeName;

    public StateStoreDurableWorkflowTimerStore(IStateStore stateStore, string storeName = "statestore")
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _storeName = storeName;
    }

    public async ValueTask SaveTimerAsync(DurableWorkflowTimerRecord timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);

        var timerKey = $"centra:workflows:timer:{timer.InstanceId.Value}";
        var dto = new DurableWorkflowTimerDto(
            timer.InstanceId.Value,
            timer.EventId,
            timer.DueTimeUtc,
            timer.CreatedAtUtc);

        await _stateStore.SetAsync(_storeName, timerKey, dto, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Optimistically update active timers index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var existing = await _stateStore.GetAsync<List<string>>(_storeName, ActiveIndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            var list = existing.HasValue ? new List<string>(existing.Value.Value) : [];
            if (!list.Contains(timer.InstanceId.Value))
            {
                list.Add(timer.InstanceId.Value);
            }

            var etag = existing?.ETag ?? string.Empty;
            var success = await _stateStore.TrySetAsync(_storeName, ActiveIndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }
    }

    public async ValueTask<IReadOnlyList<DurableWorkflowTimerRecord>> GetDueTimersAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
    {
        var existing = await _stateStore.GetAsync<List<string>>(_storeName, ActiveIndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!existing.HasValue || existing.Value.Value.Count == 0)
        {
            return Array.Empty<DurableWorkflowTimerRecord>();
        }

        var result = new List<DurableWorkflowTimerRecord>();
        foreach (var id in existing.Value.Value)
        {
            var timerKey = $"centra:workflows:timer:{id}";
            var entry = await _stateStore.GetAsync<DurableWorkflowTimerDto>(_storeName, timerKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (entry.HasValue && entry.Value.Value.DueTimeUtc <= asOfUtc)
            {
                var dto = entry.Value.Value;
                result.Add(new DurableWorkflowTimerRecord(
                    new WorkflowInstanceId(dto.InstanceId),
                    dto.EventId,
                    dto.DueTimeUtc,
                    dto.CreatedAtUtc));
            }
        }

        return result;
    }

    public async ValueTask DeleteTimerAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        var timerKey = $"centra:workflows:timer:{instanceId.Value}";
        await _stateStore.DeleteAsync(_storeName, timerKey, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Optimistically remove from active timers index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var existing = await _stateStore.GetAsync<List<string>>(_storeName, ActiveIndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!existing.HasValue)
            {
                break;
            }

            var list = new List<string>(existing.Value.Value);
            if (!list.Remove(instanceId.Value))
            {
                break;
            }

            var etag = existing.Value.ETag;
            var success = await _stateStore.TrySetAsync(_storeName, ActiveIndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }
    }
}
