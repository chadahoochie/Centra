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

    public ActorReminderManager(
        ActorIdentity identity,
        IStateStore stateStore,
        string storeName,
        ActorReminderCoordinator? coordinator = null)
    {
        _identity = identity;
        _stateStore = stateStore;
        _storeName = storeName;
        _coordinator = coordinator;
    }

    public async ValueTask<ActorReminder> RegisterReminderAsync(
        string reminderName,
        ReadOnlyMemory<byte> state,
        TimeSpan dueTime,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = FormatReminderKey(reminderName);
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

        var key = FormatReminderKey(reminderName);
        await _stateStore.DeleteAsync(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        _coordinator?.UnregisterReminder(_identity, reminderName);
    }

    public async ValueTask<ActorReminder?> GetReminderAsync(string reminderName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = FormatReminderKey(reminderName);
        var stored = await _stateStore.GetAsync<ActorReminderRecord>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (stored.HasValue)
        {
            var r = stored.Value.Value;
            return new ActorReminder(r.Name, r.DueTime, r.Period, r.State ?? Array.Empty<byte>());
        }

        return null;
    }

    private string FormatReminderKey(string reminderName) =>
        $"actor-reminders:{_identity.Type.Value}:{_identity.Id.Value}:{reminderName}";
}
