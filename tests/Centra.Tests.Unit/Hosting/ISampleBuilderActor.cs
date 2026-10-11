using Centra.Actors;

namespace Centra.Tests.Unit.Hosting;

public interface ISampleBuilderActor : IActor
{
    Task PingAsync();
}
