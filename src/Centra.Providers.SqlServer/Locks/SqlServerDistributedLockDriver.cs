using System.Data;
using System.Diagnostics;
using Centra.Drivers;
using Centra.Locks;
using Centra.Providers.SqlServer.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Centra.Providers.SqlServer.Locks;

public sealed class SqlServerDistributedLockDriver : IDistributedLockDriver
{
    private readonly SqlServerProviderOptions _options;
    private readonly string _fullTableName;
    private int _initialized;

    public SqlServerDistributedLockDriver(IOptions<SqlServerProviderOptions> options)
    {
        _options = options?.Value ?? new SqlServerProviderOptions();
        _fullTableName = $"[{_options.SchemaName}].[{_options.LockTableName}]";
    }

    private async ValueTask EnsureTableCreatedAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoCreateTable && Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            var sql = $"""
                IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = @tbl AND s.name = @sch)
                BEGIN
                    CREATE TABLE {_fullTableName} (
                        [lock_store] NVARCHAR(128) NOT NULL,
                        [resource_id] NVARCHAR(256) NOT NULL,
                        [lock_id] NVARCHAR(64) NOT NULL,
                        [acquired_at_utc] DATETIMEOFFSET NOT NULL,
                        [expires_at_utc] DATETIMEOFFSET NOT NULL,
                        CONSTRAINT [PK_{_options.LockTableName}] PRIMARY KEY CLUSTERED ([lock_store], [resource_id])
                    );
                END
                """;

            await using var conn = new SqlConnection(_options.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@tbl", SqlDbType.NVarChar, 128) { Value = _options.LockTableName });
            cmd.Parameters.Add(new SqlParameter("@sch", SqlDbType.NVarChar, 128) { Value = _options.SchemaName });
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<IDistributedLock?> TryAcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockStoreName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        var lockId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + expiryTime;

        var sql = $"""
            MERGE {_fullTableName} WITH (HOLDLOCK) AS target
            USING (SELECT @lock_store AS lock_store, @resource_id AS resource_id) AS source
            ON (target.lock_store = source.lock_store AND target.resource_id = source.resource_id)
            WHEN MATCHED AND target.expires_at_utc < @now THEN
                UPDATE SET [lock_id] = @lock_id, [acquired_at_utc] = @now, [expires_at_utc] = @expires_at_utc
            WHEN NOT MATCHED THEN
                INSERT (lock_store, resource_id, lock_id, acquired_at_utc, expires_at_utc)
                VALUES (@lock_store, @resource_id, @lock_id, @now, @expires_at_utc)
            OUTPUT inserted.lock_id;
            """;

        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.Add(new SqlParameter("@lock_store", SqlDbType.NVarChar, 128) { Value = lockStoreName });
            cmd.Parameters.Add(new SqlParameter("@resource_id", SqlDbType.NVarChar, 256) { Value = resourceId });
            cmd.Parameters.Add(new SqlParameter("@lock_id", SqlDbType.NVarChar, 64) { Value = lockId });
            cmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTimeOffset) { Value = now });
            cmd.Parameters.Add(new SqlParameter("@expires_at_utc", SqlDbType.DateTimeOffset) { Value = expiresAt });

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is string currentStr && string.Equals(currentStr, lockId, StringComparison.Ordinal))
            {
                return new SqlServerDistributedLock(_options.ConnectionString, _fullTableName, lockStoreName, resourceId, lockId);
            }
        }

        return null;
    }

    public ValueTask<IDistributedLock> AcquireLockAsync(
        string lockStoreName,
        string resourceId,
        TimeSpan expiryTime,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        DistributedLockHelper.AcquireLockAsync(this, lockStoreName, resourceId, expiryTime, timeout, cancellationToken);
}
