namespace Centra.PubSub.Routing.Rules;

internal readonly record struct RuleFilterToken(RuleFilterTokenType Type, string Value, int Position);
