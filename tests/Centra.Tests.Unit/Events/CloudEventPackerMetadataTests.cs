using System.Collections.Generic;
using Centra.Events;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Events;

public sealed class CloudEventPackerMetadataTests
{
    private sealed record PlainEventWithoutAttribute(string Message);

    [Fact]
    public void Pack_Should_Support_Subject_AdditionalMetadata_And_Unattributed_Events()
    {
        // Arrange
        var evt = new PlainEventWithoutAttribute("hello");
        var metadata = new Dictionary<string, string>
        {
            ["custom-key"] = "custom-val"
        };

        // Act
        var packed = CloudEventPacker.Pack(
            evt,
            source: "/test/source",
            mode: CloudEventMode.Binary,
            subject: "test-subject",
            additionalMetadata: metadata);

        // Assert
        packed.Headers[CloudEventConstants.TypeHeader].ShouldBe(nameof(PlainEventWithoutAttribute));
        packed.Headers[CloudEventConstants.SubjectHeader].ShouldBe("test-subject");
        packed.Headers["custom-key"].ShouldBe("custom-val");
    }
}
