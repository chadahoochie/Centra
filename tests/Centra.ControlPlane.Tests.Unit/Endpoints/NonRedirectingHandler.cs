namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class NonRedirectingHandler : DelegatingHandler
{
    public NonRedirectingHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return base.SendAsync(request, cancellationToken);
    }
}
