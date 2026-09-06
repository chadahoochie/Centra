using System.Data;
using Centra.Locks;
using Microsoft.Data.SqlClient;

namespace Centra.Providers.SqlServer.Locks;

public sealed class SqlServerDistributedLock : IDistributedLock
{
    private readonly string _connectionString;
    private readonly string _fullTableName;
    private int _disposed;

    public string LockStoreName { get; }
    public string ResourceId { get; }
    public string LockId { get; }

    public SqlServerDistributedLock(
        string connectionString,
        string fullTableName,
        string lockStoreName,
        string resourceId,
        string lockId)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _fullTableName = fullTableName ?? throw new ArgumentNullException(nameof(fullTableName));
        LockStoreName = lockStoreName ?? throw new ArgumentNullException(nameof(lockStoreName));
        ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        LockId = lockId ?? throw new ArgumentNullException(nameof(lockId));
    }

    public async ValueTask<bool> RenewAsync(TimeSpan newExpiryTime, CancellationToken cancellationToken = default)
    {
        if (_disposed != 0)
        {
            return false;
        }

        var expiresAt = DateTimeOffset.UtcNow + newExpiryTime;
        var sql = $"""
            UPDATE {_fullTableName}
            SET [expires_at_utc] = @expires_at_utc
            WHERE [lock_store] = @lock_store AND [resource_id] = @resource_id AND [lock_id] = @lock_id;
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@expires_at_utc", SqlDbType.DateTimeOffset) { Value = expiresAt });
        cmd.Parameters.Add(new SqlParameter("@lock_store", SqlDbType.NVarChar, 128) { Value = LockStoreName });
        cmd.Parameters.Add(new SqlParameter("@resource_id", SqlDbType.NVarChar, 256) { Value = ResourceId });
        cmd.Parameters.Add(new SqlParameter("@lock_id", SqlDbType.NVarChar, 64) { Value = LockId });

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var sql = $"""
            DELETE FROM {_fullTableName}
            WHERE [lock_store] = @lock_store AND [resource_id] = @resource_id AND [lock_id] = @lock_id;
            """;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@lock_store", SqlDbType.NVarChar, 128) { Value = LockStoreName });
            cmd.Parameters.Add(new SqlParameter("@resource_id", SqlDbType.NVarChar, 256) { Value = ResourceId });
            cmd.Parameters.Add(new SqlParameter("@lock_id", SqlDbType.NVarChar, 64) { Value = LockId });

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // Lock release swallows to ensure idempotent async disposal
        }
    }
}
