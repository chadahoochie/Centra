using System.Text;
using System.Text.Json;
using Centra.Events;
using Centra.PubSub.Routing.Rules;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub.Rules;

public sealed class RuleFilterEvaluatorTests
{
    private readonly RuleFilterEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_EnvelopeAttributes_MatchesCorrectly()
    {
        var context = new EventContext(
            Id: "evt-100",
            Topic: "orders",
            PubSubName: "pubsub",
            Source: "checkout-service",
            Type: "order.created.v1",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "corr-123",
            CausationId: "cause-456",
            TenantId: "tenant-xyz",
            Headers: new Dictionary<string, string>
            {
                [CloudEventConstants.SubjectHeader] = "eu-region",
                [CloudEventConstants.DataContentTypeHeader] = "application/json"
            });

        var filter = _evaluator.Compile("event.type == 'order.created.v1' && event.source == 'checkout-service'");
        filter.RequiresDataPayload.ShouldBeFalse();
        filter.Evaluate(in context, null, context.Headers).ShouldBeTrue();

        var nonMatching = _evaluator.Compile("event.type == 'order.cancelled'");
        nonMatching.Evaluate(in context, null, context.Headers).ShouldBeFalse();

        var subjectFilter = _evaluator.Compile("event.subject == 'eu-region' && event.tenantid == 'tenant-xyz'");
        subjectFilter.Evaluate(in context, null, context.Headers).ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_HeadersBracketSyntax_MatchesCorrectly()
    {
        var headers = new Dictionary<string, string>
        {
            ["ce-type"] = "user.registered",
            ["x-tier"] = "premium"
        };

        var context = new EventContext(
            Id: "1",
            Topic: "users",
            PubSubName: "bus",
            Source: "auth",
            Type: "user.registered",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: null,
            CausationId: null,
            TenantId: null,
            Headers: headers);

        var filter = _evaluator.Compile("headers['ce-type'] == 'user.registered' && headers['x-tier'] == 'premium'");
        filter.RequiresDataPayload.ShouldBeFalse();
        filter.Evaluate(in context, null, headers).ShouldBeTrue();

        var missingHeader = _evaluator.Compile("headers['x-missing'] == 'val'");
        missingHeader.Evaluate(in context, null, headers).ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_JsonDataPayload_MatchesPrimitivesAndObjects()
    {
        var json = """
        {
            "status": "approved",
            "amount": 250.50,
            "quantity": 5,
            "customer": {
                "tier": "gold",
                "active": true
            },
            "memo": null
        }
        """;

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetBytes(json));
        var root = doc.RootElement;

        var context = new EventContext(
            Id: "1",
            Topic: "orders",
            PubSubName: "bus",
            Source: "checkout",
            Type: "order.created",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: null,
            CausationId: null,
            TenantId: null,
            Headers: new Dictionary<string, string>());

        var filter = _evaluator.Compile("data.status == 'approved' && data.amount > 200 && data.quantity >= 5");
        filter.RequiresDataPayload.ShouldBeTrue();
        filter.Evaluate(in context, root, context.Headers).ShouldBeTrue();

        var nestedFilter = _evaluator.Compile("data.customer.tier == 'gold' && data.customer.active == true");
        nestedFilter.Evaluate(in context, root, context.Headers).ShouldBeTrue();

        var nullCheckFilter = _evaluator.Compile("data.memo == null && data.status != null");
        nullCheckFilter.Evaluate(in context, root, context.Headers).ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_InOperator_MatchesArrayAndStringContainment()
    {
        var json = """
        {
            "region": "US",
            "category": "electronics"
        }
        """;

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetBytes(json));
        var root = doc.RootElement;

        var context = new EventContext(
            Id: "1",
            Topic: "events",
            PubSubName: "bus",
            Source: "src",
            Type: "order.v1",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: null,
            CausationId: null,
            TenantId: null,
            Headers: new Dictionary<string, string>());

        var inFilter = _evaluator.Compile("data.region in ['US', 'CA', 'GB']");
        inFilter.Evaluate(in context, root, context.Headers).ShouldBeTrue();

        var notInFilter = _evaluator.Compile("data.region in ['FR', 'DE']");
        notInFilter.Evaluate(in context, root, context.Headers).ShouldBeFalse();

        var strInFilter = _evaluator.Compile("'v1' in event.type");
        strInFilter.Evaluate(in context, root, context.Headers).ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_StringFunctions_FunctionAndMethodStyle()
    {
        var context = new EventContext(
            Id: "1",
            Topic: "events",
            PubSubName: "bus",
            Source: "checkout",
            Type: "order.created.v1",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: null,
            CausationId: null,
            TenantId: null,
            Headers: new Dictionary<string, string>());

        var f1 = _evaluator.Compile("contains(event.type, 'created')");
        f1.Evaluate(in context, null, context.Headers).ShouldBeTrue();

        var f2 = _evaluator.Compile("event.type.startsWith('order.')");
        f2.Evaluate(in context, null, context.Headers).ShouldBeTrue();

        var f3 = _evaluator.Compile("event.type.endsWith('.v1')");
        f3.Evaluate(in context, null, context.Headers).ShouldBeTrue();

        var f4 = _evaluator.Compile("event.type.endsWith('.v2')");
        f4.Evaluate(in context, null, context.Headers).ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_ComplexCompoundExpression_EvaluatesCorrectly()
    {
        var json = """
        {
            "priority": "high",
            "amount": 5000
        }
        """;

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetBytes(json));
        var root = doc.RootElement;

        var context = new EventContext(
            Id: "1",
            Topic: "orders",
            PubSubName: "bus",
            Source: "pos",
            Type: "order.v2",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: null,
            CausationId: null,
            TenantId: null,
            Headers: new Dictionary<string, string>());

        var expr = "(event.type == 'order.v2' || event.type == 'order.v3') && (data.priority == 'high' && data.amount > 1000)";
        var filter = _evaluator.Compile(expr);

        filter.Evaluate(in context, root, context.Headers).ShouldBeTrue();
    }
}
