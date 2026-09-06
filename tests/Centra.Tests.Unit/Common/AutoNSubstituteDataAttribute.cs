using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AutoFixture.Xunit2;

namespace Centra.Tests.Unit.Common;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AutoNSubstituteDataAttribute : AutoDataAttribute
{
    public AutoNSubstituteDataAttribute()
        : base(() =>
        {
            var fixture = new Fixture();
            fixture.Customize(new AutoNSubstituteCustomization
            {
                ConfigureMembers = true,
                GenerateDelegates = true
            });
            return fixture;
        })
    {
    }
}
