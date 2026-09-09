using Centra.Actors;
using Centra.State;

namespace Centra.Core.Actors;

/// <summary>
/// Manages durable reminders for an actor backed by persistent state.
/// </summary>
public sealed class ActorReminderManager : IActorReminderManager
{
    private readonly ActorIdentity _identity;
    private readonly IStateStore _stateStore;
    private readonly string _storeName;
    private readonly ActorReminderCoordinator? _coordinator;
    private readonly IActorReminderKeyFormatter _keyFormatter;

    public ActorReminderManager(
        ActorIdentity identity,
        IStateStore stateStore,
        string storeName,
        ActorReminderCoordinator? coordinator = null,
        IActorReminderKeyFormatter? keyFormatter = null)
    {
        _identity = identity;
        _stateStore = stateStore;
        _storeName = storeName;
        _coordinator = coordinator;
        _keyFormatter = keyFormatter ?? ActorReminderKeyFormatter.Instance;
    }

    public async ValueTask<ActorReminder> RegisterReminderAsync(
        string reminderName,
        ReadOnlyMemory<byte> state,
        TimeSpan dueTime,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = _keyFormatter.FormatStorageKey(_identity, reminderName);
        var record = new ActorReminderRecord(
            reminderName,
            dueTime,
            period,
            state.ToArray());

        await _stateStore.SetAsync(_storeName, key, record, cancellationToken: cancellationToken).ConfigureAwait(false);
        _coordinator?.RegisterReminder(_identity, reminderName, dueTime, period, record.State);

        return new ActorReminder(reminderName, dueTime, period, state);
    }

    public async ValueTask UnregisterReminderAsync(string reminderName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = _keyFormatter.FormatStorageKey(_identity, reminderName);
        await _stateStore.DeleteAsync(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        _coordinator?.UnregisterReminder(_identity, reminderName);
    }

    public async ValueTask<ActorReminder?> GetReminderAsync(string reminderName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = _keyFormatter.FormatStorageKey(_identity, reminderName);
        var stored = await _stateStore.GetAsync<ActorReminderRecord>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (stored.HasValue)
        {
            var r = stored.Value.Value;
            return new ActorReminder(r.Name, r.DueTime, r.Period, r.State ?? Array.Empty<byte>());
        }

        return null;
    }
}
