using Centra.Providers.SqlServer.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.SqlServer.Tests.Unit.Options;

public sealed class SqlServerProviderOptionsTests
{
    [Fact]
    public void Default_Options_Should_Have_Expected_Values()
    {
        var options = new SqlServerProviderOptions();

        options.ConnectionString.ShouldNotBeNullOrWhiteSpace();
        options.SchemaName.ShouldBe("dbo");
        options.StateTableName.ShouldBe("centra_state");
        options.LockTableName.ShouldBe("centra_locks");
        options.AutoCreateTable.ShouldBeTrue();
        options.DefaultStateStoreName.ShouldBe("statestore");
        options.DefaultLockStoreName.ShouldBe("lockstore");
    }

    [Fact]
    public void Options_Should_Allow_Property_Mutations()
    {
        var options = new SqlServerProviderOptions
        {
            ConnectionString = "Server=my-sql-server;Database=mydb;",
            SchemaName = "custom_schema",
            StateTableName = "app_state",
            LockTableName = "app_locks",
            AutoCreateTable = false,
            DefaultStateStoreName = "custom-state",
            DefaultLockStoreName = "custom-lock"
        };

        options.ConnectionString.ShouldBe("Server=my-sql-server;Database=mydb;");
        options.SchemaName.ShouldBe("custom_schema");
        options.StateTableName.ShouldBe("app_state");
        options.LockTableName.ShouldBe("app_locks");
        options.AutoCreateTable.ShouldBeFalse();
        options.DefaultStateStoreName.ShouldBe("custom-state");
        options.DefaultLockStoreName.ShouldBe("custom-lock");
    }
}
