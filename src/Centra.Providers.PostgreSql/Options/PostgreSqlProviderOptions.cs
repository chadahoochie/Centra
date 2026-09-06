namespace Centra.Providers.PostgreSql.Options;

public sealed class PostgreSqlProviderOptions
{
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=centra;Username=postgres;Password=postgres";
    public string SchemaName { get; set; } = "public";
    public string StateTableName { get; set; } = "centra_state";
    public string LockTableName { get; set; } = "centra_locks";
    public bool AutoCreateTable { get; set; } = true;
    public string DefaultStateStoreName { get; set; } = "statestore";
    public string DefaultLockStoreName { get; set; } = "lockstore";
}
