using Centra.Actors;

namespace Centra.Tests.Unit.Hosting;

public sealed class SampleBuilderActor : Actor, ISampleBuilderActor
{
    public Task PingAsync() => Task.CompletedTask;
}
