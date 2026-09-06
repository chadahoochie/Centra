using Centra.Providers.CosmosDb.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.CosmosDb.Tests.Unit.Options;

public sealed class CosmosDbProviderOptionsTests
{
    [Fact]
    public void Default_Options_Should_Have_Expected_Values()
    {
        var options = new CosmosDbProviderOptions();

        options.ConnectionString.ShouldNotBeNullOrWhiteSpace();
        options.DatabaseName.ShouldBe("centra");
        options.StateContainerName.ShouldBe("centra_state");
        options.LockContainerName.ShouldBe("centra_locks");
        options.StatePartitionKeyPath.ShouldBe("/storeName");
        options.LockPartitionKeyPath.ShouldBe("/lockStore");
        options.AutoCreateDatabaseAndContainers.ShouldBeTrue();
        options.Throughput.ShouldBe(400);
        options.DefaultStateStoreName.ShouldBe("statestore");
        options.DefaultLockStoreName.ShouldBe("lockstore");
    }

    [Fact]
    public void Options_Should_Allow_Property_Mutations()
    {
        var options = new CosmosDbProviderOptions
        {
            ConnectionString = "AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=key;",
            DatabaseName = "custom_db",
            StateContainerName = "items",
            LockContainerName = "leases",
            StatePartitionKeyPath = "/partition",
            LockPartitionKeyPath = "/leaseGroup",
            AutoCreateDatabaseAndContainers = false,
            Throughput = 1000,
            DefaultStateStoreName = "custom-state",
            DefaultLockStoreName = "custom-lock"
        };

        options.ConnectionString.ShouldBe("AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=key;");
        options.DatabaseName.ShouldBe("custom_db");
        options.StateContainerName.ShouldBe("items");
        options.LockContainerName.ShouldBe("leases");
        options.StatePartitionKeyPath.ShouldBe("/partition");
        options.LockPartitionKeyPath.ShouldBe("/leaseGroup");
        options.AutoCreateDatabaseAndContainers.ShouldBeFalse();
        options.Throughput.ShouldBe(1000);
        options.DefaultStateStoreName.ShouldBe("custom-state");
        options.DefaultLockStoreName.ShouldBe("custom-lock");
    }
}
