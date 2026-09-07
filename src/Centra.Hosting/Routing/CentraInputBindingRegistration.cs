namespace Centra.Hosting.Routing;

public sealed record CentraInputBindingRegistration(
    string BindingName,
    Type HandlerType);
