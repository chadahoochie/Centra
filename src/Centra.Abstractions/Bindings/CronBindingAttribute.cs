namespace Centra.Bindings;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CronBindingAttribute(string cronExpression) : Attribute
{
    public string CronExpression { get; } = cronExpression;
}
