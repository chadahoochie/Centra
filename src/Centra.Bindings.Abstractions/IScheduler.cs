namespace Centra.Bindings;

public interface IScheduler
{
    void ScheduleCron(string jobName, string cronExpression, IJobHandler handler, CronScheduleOptions? options = null);
    void ScheduleInterval(string jobName, TimeSpan interval, IJobHandler handler);
    bool Unregister(string jobName);
}
