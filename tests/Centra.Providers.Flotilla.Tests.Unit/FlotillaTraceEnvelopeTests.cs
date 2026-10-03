using System.Diagnostics;
using System.Text;
using Centra.Providers.Flotilla.Protocol;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaTraceEnvelopeTests
{
    [Fact]
    public void IsEnveloped_ValidEnvelope_ReturnsTrue()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var context = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
        var payload = Encoding.UTF8.GetBytes("test payload");

        var enveloped = FlotillaTraceEnvelope.Wrap(context, payload);

        FlotillaTraceEnvelope.IsEnveloped(enveloped).ShouldBeTrue();
    }

    [Fact]
    public void IsEnveloped_InvalidOrShortBuffer_ReturnsFalse()
    {
        FlotillaTraceEnvelope.IsEnveloped(ReadOnlySpan<byte>.Empty).ShouldBeFalse();
        FlotillaTraceEnvelope.IsEnveloped(new byte[] { 0x54, 0x45 }).ShouldBeFalse();

        var invalidMagic = new byte[40];
        invalidMagic[0] = 0x00;
        invalidMagic[1] = 0x00;
        FlotillaTraceEnvelope.IsEnveloped(invalidMagic).ShouldBeFalse();
    }

    [Fact]
    public void WrapAndUnwrap_Roundtrip_PreservesTraceContextAndPayload()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var context = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
        var payload = Encoding.UTF8.GetBytes("payload content for consensus");

        var enveloped = FlotillaTraceEnvelope.Wrap(context, payload);
        enveloped.Length.ShouldBe(FlotillaTraceEnvelope.HeaderSize + payload.Length);

        var (unwrappedContext, unwrappedPayload) = FlotillaTraceEnvelope.Unwrap(enveloped);

        unwrappedContext.TraceId.ShouldBe(context.TraceId);
        unwrappedContext.SpanId.ShouldBe(context.SpanId);
        unwrappedContext.TraceFlags.ShouldBe(ActivityTraceFlags.Recorded);
        unwrappedPayload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void WrapAndUnwrap_DefaultContext_ReturnsDefaultContext()
    {
        var payload = Encoding.UTF8.GetBytes("no-trace payload");

        var enveloped = FlotillaTraceEnvelope.Wrap(default, payload);
        enveloped.Length.ShouldBe(FlotillaTraceEnvelope.HeaderSize + payload.Length);

        var (unwrappedContext, unwrappedPayload) = FlotillaTraceEnvelope.Unwrap(enveloped);

        unwrappedContext.ShouldBe(default);
        unwrappedPayload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void Unwrap_NotEnveloped_ReturnsOriginalPayloadAndDefaultContext()
    {
        var plain = Encoding.UTF8.GetBytes("plain non-enveloped payload");

        var (context, payload) = FlotillaTraceEnvelope.Unwrap(plain);

        context.ShouldBe(default);
        payload.ToArray().ShouldBe(plain);
    }

    [Fact]
    public void Unwrap_TruncatedPayload_ThrowsInvalidDataException()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var context = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
        var payload = Encoding.UTF8.GetBytes("longer payload that will be truncated");

        var enveloped = FlotillaTraceEnvelope.Wrap(context, payload);
        // Truncate by 5 bytes
        var truncated = enveloped.AsMemory(0, enveloped.Length - 5);

        Should.Throw<InvalidDataException>(() => FlotillaTraceEnvelope.Unwrap(truncated));
    }
}
