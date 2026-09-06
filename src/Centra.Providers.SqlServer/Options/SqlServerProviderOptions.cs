namespace Centra.Providers.SqlServer.Options;

public sealed class SqlServerProviderOptions
{
    public string ConnectionString { get; set; } = "Server=localhost,1433;Database=centra;User Id=sa;Password=Your_password123;TrustServerCertificate=True;";
    public string SchemaName { get; set; } = "dbo";
    public string StateTableName { get; set; } = "centra_state";
    public string LockTableName { get; set; } = "centra_locks";
    public bool AutoCreateTable { get; set; } = true;
    public string DefaultStateStoreName { get; set; } = "statestore";
    public string DefaultLockStoreName { get; set; } = "lockstore";
}
