using Centra.PubSub.Routing.Rules;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub.Rules;

public sealed class RuleFilterParserTests
{
    [Fact]
    public void Parse_EmptyTokens_ReturnsConstantTrue()
    {
        var parser = new RuleFilterParser([]);
        var expr = parser.Parse();

        expr.ShouldBeOfType<ConstantRuleExpression>();
        var val = expr.Evaluate(default, null, new Dictionary<string, string>());
        val.ShouldBe(true);
    }

    [Fact]
    public void Parse_SimpleEquality_ReturnsComparisonRuleExpression()
    {
        var expr = RuleFilterParser.ParseExpression("event.type == 'order.v1'");
        expr.ShouldBeOfType<ComparisonRuleExpression>();

        var comp = (ComparisonRuleExpression)expr;
        comp.Operator.ShouldBe(RuleFilterTokenType.Equals);
        comp.Left.ShouldBeOfType<PropertyAccessRuleExpression>();
        comp.Right.ShouldBeOfType<ConstantRuleExpression>();
    }

    [Fact]
    public void Parse_LogicalAndOrPrecedence_ConstructsCorrectAst()
    {
        // a || b && c should parse as a || (b && c)
        var expr = RuleFilterParser.ParseExpression("true || false && false");
        expr.ShouldBeOfType<BinaryRuleExpression>();

        var bin = (BinaryRuleExpression)expr;
        bin.Operator.ShouldBe(RuleFilterTokenType.Or);
        bin.Left.ShouldBeOfType<ConstantRuleExpression>();
        bin.Right.ShouldBeOfType<BinaryRuleExpression>();
    }

    [Fact]
    public void Parse_ParenthesesGrouping_OverridesPrecedence()
    {
        // (true || false) && false
        var expr = RuleFilterParser.ParseExpression("(true || false) && false");
        expr.ShouldBeOfType<BinaryRuleExpression>();

        var bin = (BinaryRuleExpression)expr;
        bin.Operator.ShouldBe(RuleFilterTokenType.And);
        bin.Left.ShouldBeOfType<BinaryRuleExpression>();
        bin.Right.ShouldBeOfType<ConstantRuleExpression>();
    }

    [Fact]
    public void Parse_UnaryNot_ReturnsUnaryRuleExpression()
    {
        var expr = RuleFilterParser.ParseExpression("!true");
        expr.ShouldBeOfType<UnaryRuleExpression>();
        var unary = (UnaryRuleExpression)expr;
        unary.Operator.ShouldBe(RuleFilterTokenType.Not);
    }

    [Fact]
    public void Parse_FunctionCall_ReturnsFunctionCallRuleExpression()
    {
        var expr = RuleFilterParser.ParseExpression("contains(event.type, 'order')");
        expr.ShouldBeOfType<FunctionCallRuleExpression>();
        var func = (FunctionCallRuleExpression)expr;
        func.FunctionName.ShouldBe("contains");
        func.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Parse_MethodCallStyle_ReturnsFunctionCallRuleExpression()
    {
        var expr = RuleFilterParser.ParseExpression("event.type.startsWith('order.')");
        expr.ShouldBeOfType<FunctionCallRuleExpression>();
        var func = (FunctionCallRuleExpression)expr;
        func.FunctionName.ShouldBe("startsWith");
        func.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Parse_ArrayLiteralInExpression_ParsesCorrectly()
    {
        var expr = RuleFilterParser.ParseExpression("data.status in ['pending', 'approved']");
        expr.ShouldBeOfType<ComparisonRuleExpression>();
        var comp = (ComparisonRuleExpression)expr;
        comp.Operator.ShouldBe(RuleFilterTokenType.In);
        comp.Right.ShouldBeOfType<ConstantRuleExpression>();
    }

    [Fact]
    public void Parse_MissingClosingParenthesis_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => RuleFilterParser.ParseExpression("(true && false"));
    }

    [Fact]
    public void Parse_MissingClosingBracket_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => RuleFilterParser.ParseExpression("['a', 'b'"));
    }

    [Fact]
    public void Parse_UnexpectedTrailingTokens_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => RuleFilterParser.ParseExpression("true false"));
    }
}
