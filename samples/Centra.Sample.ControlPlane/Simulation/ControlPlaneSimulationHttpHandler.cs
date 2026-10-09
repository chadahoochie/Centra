using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Centra.Sample.ControlPlane.Simulation;

public sealed class ControlPlaneSimulationHttpHandler : DelegatingHandler
{
    public ControlPlaneSimulationHttpHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return base.SendAsync(request, cancellationToken);
    }
}
