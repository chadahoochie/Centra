using Centra.Actors;

namespace Centra.Sample.Actors.Domain;

/// <summary>
/// High-performance virtual actor managing account balance state and monthly interest reminders.
/// </summary>
public sealed class AccountActor : Actor, IAccountActor
{
    public const string BalanceKey = "account_balance";
    public const string InterestReminderName = "monthly-interest";

    public async ValueTask<decimal> GetBalanceAsync()
    {
        var balance = await StateManager.GetStateAsync<decimal>(BalanceKey).ConfigureAwait(false);
        return balance;
    }

    public async ValueTask<decimal> DepositAsync(decimal amount)
    {
        var current = await StateManager.GetStateAsync<decimal>(BalanceKey).ConfigureAwait(false);
        var updated = current + amount;
        await StateManager.SetStateAsync(BalanceKey, updated).ConfigureAwait(false);
        return updated;
    }

    public async ValueTask<bool> WithdrawAsync(decimal amount)
    {
        var current = await StateManager.GetStateAsync<decimal>(BalanceKey).ConfigureAwait(false);
        if (current < amount)
        {
            return false;
        }

        await StateManager.SetStateAsync(BalanceKey, current - amount).ConfigureAwait(false);
        return true;
    }

    public async ValueTask ScheduleInterestReminderAsync(TimeSpan period)
    {
        await Reminders.RegisterReminderAsync(InterestReminderName, period, period).ConfigureAwait(false);
    }

    public async ValueTask ReceiveReminderAsync(
        string reminderName,
        ReadOnlyMemory<byte> state,
        TimeSpan dueTime,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        if (reminderName == InterestReminderName)
        {
            var current = await StateManager.GetStateAsync<decimal>(BalanceKey, cancellationToken).ConfigureAwait(false);
            var interest = Math.Round(current * 0.05m, 2); // 5% interest
            await StateManager.SetStateAsync(BalanceKey, current + interest, cancellationToken).ConfigureAwait(false);
        }
    }
}
