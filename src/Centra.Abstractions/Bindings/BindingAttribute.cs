namespace Centra.Bindings;

[AttributeUsage(AttributeTargets.Method)]
public sealed class BindingAttribute(string bindingName) : Attribute
{
    public string BindingName { get; } = bindingName;
}
