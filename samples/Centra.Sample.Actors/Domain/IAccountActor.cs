using Centra.Actors;

namespace Centra.Sample.Actors.Domain;

/// <summary>
/// Virtual actor contract representing a bank account with optimistic turn-based state updates and durable reminders.
/// </summary>
public interface IAccountActor : IActor, IRemindable
{
    ValueTask<decimal> GetBalanceAsync();
    ValueTask<decimal> DepositAsync(decimal amount);
    ValueTask<bool> WithdrawAsync(decimal amount);
    ValueTask ScheduleInterestReminderAsync(TimeSpan period);
}
