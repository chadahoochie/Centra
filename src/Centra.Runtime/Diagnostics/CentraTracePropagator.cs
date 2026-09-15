using System.Diagnostics;
using Centra.Events;

namespace Centra.Diagnostics;

public static class CentraTracePropagator
{
    private static readonly DistributedContextPropagator Propagator = DistributedContextPropagator.Current;

    public static void Inject(Activity? activity, IDictionary<string, string> carrier)
    {
        if (activity is null)
        {
            return;
        }

        Propagator.Inject(activity, carrier, static (carrierDict, key, value) =>
        {
            if (carrierDict is IDictionary<string, string> dict)
            {
                dict[key] = value;
            }
        });
    }

    public static void Inject<TCarrier>(Activity? activity, TCarrier carrier, Action<TCarrier, string, string> setter)
    {
        if (activity is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(setter);

        Propagator.Inject(activity, carrier, (c, key, value) =>
        {
            if (c is TCarrier typed)
            {
                setter(typed, key, value);
            }
        });
    }

    public static ActivityContext Extract(IReadOnlyDictionary<string, string> carrier)
    {
        Propagator.ExtractTraceIdAndState(
            carrier,
            static (object? carrierDict, string key, out string? value, out IEnumerable<string>? values) =>
            {
                values = null;
                if (carrierDict is IReadOnlyDictionary<string, string> dict && dict.TryGetValue(key, out var val))
                {
                    value = val;
                }
                else
                {
                    value = null;
                }
            },
            out var traceParent,
            out var traceState);

        if (!string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var context))
        {
            return context;
        }

        return default;
    }
}
