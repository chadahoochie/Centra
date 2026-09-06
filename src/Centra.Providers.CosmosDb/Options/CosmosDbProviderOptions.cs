namespace Centra.Providers.CosmosDb.Options;

public sealed class CosmosDbProviderOptions
{
    public string ConnectionString { get; set; } = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    public string DatabaseName { get; set; } = "centra";
    public string StateContainerName { get; set; } = "centra_state";
    public string LockContainerName { get; set; } = "centra_locks";
    public string StatePartitionKeyPath { get; set; } = "/storeName";
    public string LockPartitionKeyPath { get; set; } = "/lockStore";
    public bool AutoCreateDatabaseAndContainers { get; set; } = true;
    public int Throughput { get; set; } = 400;
    public string DefaultStateStoreName { get; set; } = "statestore";
    public string DefaultLockStoreName { get; set; } = "lockstore";
}
