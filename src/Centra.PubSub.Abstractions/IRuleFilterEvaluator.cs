namespace Centra.PubSub;

public interface IRuleFilterEvaluator
{
    ICompiledRuleFilter Compile(string expression);
}
