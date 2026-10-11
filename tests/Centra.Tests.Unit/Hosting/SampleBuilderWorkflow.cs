using Centra.Workflows;

namespace Centra.Tests.Unit.Hosting;

public sealed class SampleBuilderWorkflow : Workflow<string, string>
{
    public override ValueTask<string> RunAsync(IWorkflowContext context, string input) =>
        ValueTask.FromResult(input);
}
