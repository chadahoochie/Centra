using System.Text.Json;
using Centra.Events;

namespace Centra.PubSub;

public interface ICompiledRuleFilter
{
    string Expression { get; }

    bool RequiresDataPayload { get; }

    bool Evaluate(in EventContext context, JsonElement? data, IReadOnlyDictionary<string, string> headers);
}
