namespace Centra.Bindings.Routing;

public sealed record CentraInputBindingRegistration(
    string BindingName,
    Type HandlerType);
