using Azure.Messaging.ServiceBus;
using Centra.Events;
using Centra.Providers.AzureServiceBus.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Providers.AzureServiceBus.Tests.Unit.PubSub;

public sealed class ServiceBusHeaderExtractorTests
{
    [Fact]
    public void ExtractHeaders_ThrowsArgumentNullException_WhenMessageIsNull()
    {
        Should.Throw<ArgumentNullException>(() =>
            ServiceBusHeaderExtractor.Instance.ExtractHeaders(null!));
    }

    [Fact]
    public void ExtractHeaders_PopulatesApplicationPropertiesAndSystemProperties()
    {
        var appProperties = new Dictionary<string, object>
        {
            ["custom-header"] = "custom-value",
            ["number-prop"] = 42
        };

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("test payload"),
            messageId: "msg-123",
            correlationId: "corr-456",
            subject: "test-topic",
            contentType: "application/json",
            properties: appProperties);

        var headers = ServiceBusHeaderExtractor.Instance.ExtractHeaders(message);

        headers.ShouldNotBeNull();
        headers["custom-header"].ShouldBe("custom-value");
        headers["number-prop"].ShouldBe("42");
        headers[CloudEventConstants.IdHeader].ShouldBe("msg-123");
        headers[CloudEventConstants.CorrelationIdHeader].ShouldBe("corr-456");
        headers[CloudEventConstants.SubjectHeader].ShouldBe("test-topic");
        headers[CloudEventConstants.DataContentTypeHeader].ShouldBe("application/json");
    }

    [Fact]
    public void ExtractHeaders_DoesNotOverwriteExistingPropertiesWithSystemProperties()
    {
        var appProperties = new Dictionary<string, object>
        {
            [CloudEventConstants.IdHeader] = "explicit-id",
            [CloudEventConstants.SubjectHeader] = "explicit-subject",
            [CloudEventConstants.CorrelationIdHeader] = "explicit-corr",
            [CloudEventConstants.DataContentTypeHeader] = "text/plain"
        };

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("test"),
            messageId: "system-id",
            correlationId: "system-corr",
            subject: "system-subject",
            contentType: "application/json",
            properties: appProperties);

        var headers = ServiceBusHeaderExtractor.Instance.ExtractHeaders(message);

        headers[CloudEventConstants.IdHeader].ShouldBe("explicit-id");
        headers[CloudEventConstants.SubjectHeader].ShouldBe("explicit-subject");
        headers[CloudEventConstants.CorrelationIdHeader].ShouldBe("explicit-corr");
        headers[CloudEventConstants.DataContentTypeHeader].ShouldBe("text/plain");
    }

    [Fact]
    public void CopyHeaderIfPresent_DoesNotAddWhenValueIsNullOrEmpty()
    {
        var dict = new Dictionary<string, string>();

        ServiceBusHeaderExtractor.CopyHeaderIfPresent(dict, "key1", null);
        ServiceBusHeaderExtractor.CopyHeaderIfPresent(dict, "key2", string.Empty);

        dict.ShouldBeEmpty();
    }

    [Fact]
    public void CopyHeaderIfPresent_AddsWhenValueIsPresentAndKeyDoesNotExist()
    {
        var dict = new Dictionary<string, string>();

        ServiceBusHeaderExtractor.CopyHeaderIfPresent(dict, "key1", "val1");

        dict["key1"].ShouldBe("val1");
    }
}
