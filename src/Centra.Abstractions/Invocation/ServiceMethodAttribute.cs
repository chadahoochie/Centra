namespace Centra.Invocation;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ServiceMethodAttribute(string method, string? httpVerb = null) : Attribute
{
    public string Method { get; } = method;
    public string HttpVerb { get; } = httpVerb ?? "POST";
}
