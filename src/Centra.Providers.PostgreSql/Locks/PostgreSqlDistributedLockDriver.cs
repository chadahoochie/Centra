using System.Diagnostics;
using Centra.Drivers;
using Centra.Locks;
using Centra.Providers.PostgreSql.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Centra.Providers.PostgreSql.Locks;

public sealed class PostgreSqlDistributedLockDriver : IDistributedLockDriver
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlProviderOptions _options;
    private readonly string _fullTableName;
    private int _initialized;

    public PostgreSqlDistributedLockDriver(
        NpgsqlDataSource dataSource,
        IOptions<PostgreSqlProviderOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? new PostgreSqlProviderOptions();
        _fullTableName = $"{_options.SchemaName}.{_options.LockTableName}";
    }

    private async ValueTask EnsureTableCreatedAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoCreateTable && Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            var sql = $"""
                CREATE TABLE IF NOT EXISTS {_fullTableName} (
                    lock_store VARCHAR(128) NOT NULL,
                    resource_id VARCHAR(256) NOT NULL,
                    lock_id VARCHAR(64) NOT NULL,
                    acquired_at_utc TIMESTAMPTZ NOT NULL,
                    expires_at_utc TIMESTAMPTZ NOT NULL,
                    PRIMARY KEY (lock_store, resource_id)
                );
                """;

            await using var cmd = _dataSource.CreateCommand(sql);
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

        var upsertSql = $"""
            INSERT INTO {_fullTableName} (lock_store, resource_id, lock_id, acquired_at_utc, expires_at_utc)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (lock_store, resource_id) DO UPDATE SET
                lock_id = EXCLUDED.lock_id,
                acquired_at_utc = EXCLUDED.acquired_at_utc,
                expires_at_utc = EXCLUDED.expires_at_utc
            WHERE {_fullTableName}.expires_at_utc < $4
            RETURNING lock_id;
            """;

        await using (var cmd = _dataSource.CreateCommand(upsertSql))
        {
            cmd.Parameters.AddWithValue(lockStoreName);
            cmd.Parameters.AddWithValue(resourceId);
            cmd.Parameters.AddWithValue(lockId);
            cmd.Parameters.AddWithValue(now);
            cmd.Parameters.AddWithValue(expiresAt);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is string currentStr && string.Equals(currentStr, lockId, StringComparison.Ordinal))
            {
                return new PostgreSqlDistributedLock(_dataSource, _fullTableName, lockStoreName, resourceId, lockId);
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
