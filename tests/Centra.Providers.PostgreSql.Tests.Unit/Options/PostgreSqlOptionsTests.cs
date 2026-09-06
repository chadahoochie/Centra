using Centra.Providers.PostgreSql.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.PostgreSql.Tests.Unit.Options;

public sealed class PostgreSqlOptionsTests
{
    [Fact]
    public void Should_Have_Sensible_Defaults()
    {
        var options = new PostgreSqlProviderOptions();

        options.ConnectionString.ShouldContain("Host=localhost;Port=5432");
        options.SchemaName.ShouldBe("public");
        options.StateTableName.ShouldBe("centra_state");
        options.LockTableName.ShouldBe("centra_locks");
        options.AutoCreateTable.ShouldBeTrue();
        options.DefaultStateStoreName.ShouldBe("statestore");
        options.DefaultLockStoreName.ShouldBe("lockstore");
    }
}
