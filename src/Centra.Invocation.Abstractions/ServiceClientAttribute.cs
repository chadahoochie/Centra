namespace Centra.Invocation;

[AttributeUsage(AttributeTargets.Interface)]
public sealed class ServiceClientAttribute(string appId) : Attribute
{
    public string AppId { get; } = appId;
}
