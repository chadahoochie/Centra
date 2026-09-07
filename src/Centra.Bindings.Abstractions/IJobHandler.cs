namespace Centra.Bindings;

public interface IJobHandler
{
    ValueTask ExecuteAsync(ScheduledJobContext context);
}
