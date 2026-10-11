using Centra.Workflows;

namespace Centra.Tests.Unit.Hosting;

public sealed class SampleBuilderActivity : WorkflowActivity<string, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, string input) =>
        ValueTask.FromResult(input);
}
