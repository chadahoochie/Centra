using AutoFixture.Xunit2;

namespace Centra.Tests.Unit.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineAutoNSubstituteDataAttribute : InlineAutoDataAttribute
{
    public InlineAutoNSubstituteDataAttribute(params object?[]? values)
        : base(new AutoNSubstituteDataAttribute(), values ?? [])
    {
    }
}
