using System.Collections.Concurrent;

namespace Centra.PubSub.Routing.Rules;

public sealed class RuleFilterEvaluator : IRuleFilterEvaluator
{
    private readonly ConcurrentDictionary<string, ICompiledRuleFilter> _cache = new(StringComparer.Ordinal);

    public ICompiledRuleFilter Compile(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        return _cache.GetOrAdd(expression, static expr =>
        {
            var parsed = RuleFilterParser.ParseExpression(expr);
            return new CompiledRuleFilter(expr, parsed);
        });
    }
}
