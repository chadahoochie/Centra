namespace Centra.Bindings;

[AttributeUsage(AttributeTargets.Method)]
public sealed class BindingAttribute(string bindingName, BindingDirection direction = BindingDirection.Input) : Attribute
{
    public string BindingName { get; } = bindingName;
    public BindingDirection Direction { get; } = direction;
}
