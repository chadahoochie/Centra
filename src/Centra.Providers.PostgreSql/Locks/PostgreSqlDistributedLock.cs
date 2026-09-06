using Centra.Locks;
using Npgsql;

namespace Centra.Providers.PostgreSql.Locks;

public sealed class PostgreSqlDistributedLock : IDistributedLock
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _fullTableName;
    private readonly string _lockStoreName;
    private int _disposed;

    public string ResourceId { get; }
    public string LockId { get; }

    public PostgreSqlDistributedLock(
        NpgsqlDataSource dataSource,
        string fullTableName,
        string lockStoreName,
        string resourceId,
        string lockId)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _fullTableName = fullTableName ?? throw new ArgumentNullException(nameof(fullTableName));
        _lockStoreName = lockStoreName ?? throw new ArgumentNullException(nameof(lockStoreName));
        ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        LockId = lockId ?? throw new ArgumentNullException(nameof(lockId));
    }

    public async ValueTask<bool> RenewAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default)
    {
        if (_disposed != 0)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var newExpires = now + additionalTime;

        var sql = $"UPDATE {_fullTableName} SET expires_at_utc = $1 WHERE lock_store = $2 AND resource_id = $3 AND lock_id = $4 AND expires_at_utc > $5;";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(newExpires);
        cmd.Parameters.AddWithValue(_lockStoreName);
        cmd.Parameters.AddWithValue(ResourceId);
        cmd.Parameters.AddWithValue(LockId);
        cmd.Parameters.AddWithValue(now);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            var sql = $"DELETE FROM {_fullTableName} WHERE lock_store = $1 AND resource_id = $2 AND lock_id = $3;";
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue(_lockStoreName);
            cmd.Parameters.AddWithValue(ResourceId);
            cmd.Parameters.AddWithValue(LockId);

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress errors on dispose
        }
    }
}
