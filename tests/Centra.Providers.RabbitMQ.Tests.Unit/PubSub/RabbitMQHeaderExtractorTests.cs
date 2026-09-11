using System.Text;
using Centra.Providers.RabbitMQ.PubSub;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class RabbitMQHeaderExtractorTests
{
    [Fact]
    public void ExtractHeaders_ReturnsEmptyDictionary_WhenPropertiesIsNull()
    {
        var headers = RabbitMQHeaderExtractor.Instance.ExtractHeaders(null);

        headers.ShouldNotBeNull();
        headers.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractHeaders_ReturnsEmptyDictionary_WhenHeadersIsNull()
    {
        var props = Substitute.For<IReadOnlyBasicProperties>();
        props.Headers.Returns((IDictionary<string, object?>?)null);

        var headers = RabbitMQHeaderExtractor.Instance.ExtractHeaders(props);

        headers.ShouldNotBeNull();
        headers.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractHeaders_DecodesByteArrayHeaders_AndConvertsObjectsToString()
    {
        var props = Substitute.For<IReadOnlyBasicProperties>();
        var headerDict = new Dictionary<string, object?>
        {
            ["ce-type"] = Encoding.UTF8.GetBytes("OrderCreated"),
            ["ce-id"] = "order-123",
            ["number-val"] = 999,
            ["null-val"] = null
        };
        props.Headers.Returns(headerDict);

        var headers = RabbitMQHeaderExtractor.Instance.ExtractHeaders(props);

        headers.ShouldNotBeNull();
        headers["ce-type"].ShouldBe("OrderCreated");
        headers["ce-id"].ShouldBe("order-123");
        headers["number-val"].ShouldBe("999");
        headers.ContainsKey("null-val").ShouldBeFalse();
    }
}
