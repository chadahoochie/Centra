using Centra.Drivers;
using Centra.Providers.PostgreSql.Options;
using Centra.State;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Centra.Providers.PostgreSql.State;

public sealed class PostgreSqlStateStoreDriver : IStateStoreDriver
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlProviderOptions _options;
    private readonly string _fullTableName;
    private int _initialized;

    public PostgreSqlStateStoreDriver(
        NpgsqlDataSource dataSource,
        IOptions<PostgreSqlProviderOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? new PostgreSqlProviderOptions();
        _fullTableName = $"{_options.SchemaName}.{_options.StateTableName}";
    }

    private async ValueTask EnsureTableCreatedAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoCreateTable && Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            var sql = $"""
                CREATE TABLE IF NOT EXISTS {_fullTableName} (
                    store_name VARCHAR(128) NOT NULL,
                    key VARCHAR(256) NOT NULL,
                    value BYTEA NOT NULL,
                    etag VARCHAR(64) NOT NULL,
                    metadata TEXT,
                    expire_at_utc TIMESTAMPTZ,
                    updated_at_utc TIMESTAMPTZ NOT NULL,
                    PRIMARY KEY (store_name, key)
                );
                CREATE INDEX IF NOT EXISTS idx_{_options.StateTableName}_expire ON {_fullTableName} (expire_at_utc) WHERE expire_at_utc IS NOT NULL;
                """;

            await using var cmd = _dataSource.CreateCommand(sql);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<StateEntry<byte[]>?> GetAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var sql = $"""
            SELECT value, etag FROM {_fullTableName}
            WHERE store_name = $1 AND key = $2 AND (expire_at_utc IS NULL OR expire_at_utc > $3);
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(storeName);
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(now);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            byte[] value = (byte[])reader[0];
            string etag = reader.GetString(1);
            return new StateEntry<byte[]>(key, value, etag, null);
        }

        return null;
    }

    public async ValueTask SetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        var newEtag = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = options?.TimeToLive.HasValue == true
            ? now + options.TimeToLive.Value
            : null;

        var sql = $"""
            INSERT INTO {_fullTableName} (store_name, key, value, etag, metadata, expire_at_utc, updated_at_utc)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (store_name, key) DO UPDATE SET
                value = EXCLUDED.value,
                etag = EXCLUDED.etag,
                metadata = EXCLUDED.metadata,
                expire_at_utc = EXCLUDED.expire_at_utc,
                updated_at_utc = EXCLUDED.updated_at_utc;
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(storeName);
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Bytea, value);
        cmd.Parameters.AddWithValue(newEtag);
        cmd.Parameters.AddWithValue(DBNull.Value);
        cmd.Parameters.AddWithValue(expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(now);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TrySetAsync(
        string storeName,
        string key,
        ReadOnlyMemory<byte> value,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        var newEtag = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = options?.TimeToLive.HasValue == true
            ? now + options.TimeToLive.Value
            : null;

        var sql = $"""
            UPDATE {_fullTableName} SET
                value = $1,
                etag = $2,
                expire_at_utc = $3,
                updated_at_utc = $4
            WHERE store_name = $5 AND key = $6 AND etag = $7;
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Bytea, value);
        cmd.Parameters.AddWithValue(newEtag);
        cmd.Parameters.AddWithValue(expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(now);
        cmd.Parameters.AddWithValue(storeName);
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(expectedETag);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async ValueTask DeleteAsync(
        string storeName,
        string key,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"DELETE FROM {_fullTableName} WHERE store_name = $1 AND key = $2;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(storeName);
        cmd.Parameters.AddWithValue(key);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryDeleteAsync(
        string storeName,
        string key,
        string expectedETag,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"DELETE FROM {_fullTableName} WHERE store_name = $1 AND key = $2 AND etag = $3;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(storeName);
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(expectedETag);

        var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async ValueTask ExecuteTransactionAsync(
        string storeName,
        IReadOnlyList<StateTransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            return;
        }

        await EnsureTableCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        foreach (var op in operations)
        {
            switch (op)
            {
                case SetTransactionOperation<byte[]> setOp:
                    var newEtag = Guid.NewGuid().ToString("N");
                    DateTimeOffset? expiresAt = setOp.Options?.TimeToLive.HasValue == true
                        ? now + setOp.Options.TimeToLive.Value
                        : null;

                    var setSql = $"""
                        INSERT INTO {_fullTableName} (store_name, key, value, etag, metadata, expire_at_utc, updated_at_utc)
                        VALUES ($1, $2, $3, $4, $5, $6, $7)
                        ON CONFLICT (store_name, key) DO UPDATE SET
                            value = EXCLUDED.value,
                            etag = EXCLUDED.etag,
                            metadata = EXCLUDED.metadata,
                            expire_at_utc = EXCLUDED.expire_at_utc,
                            updated_at_utc = EXCLUDED.updated_at_utc;
                        """;

                    await using (var setCmd = new NpgsqlCommand(setSql, conn, tx))
                    {
                        setCmd.Parameters.AddWithValue(storeName);
                        setCmd.Parameters.AddWithValue(setOp.Key);
                        setCmd.Parameters.AddWithValue(NpgsqlDbType.Bytea, setOp.Value);
                        setCmd.Parameters.AddWithValue(newEtag);
                        setCmd.Parameters.AddWithValue(DBNull.Value);
                        setCmd.Parameters.AddWithValue(expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);
                        setCmd.Parameters.AddWithValue(now);
                        await setCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;

                case DeleteTransactionOperation delOp:
                    var delSql = $"DELETE FROM {_fullTableName} WHERE store_name = $1 AND key = $2;";
                    await using (var delCmd = new NpgsqlCommand(delSql, conn, tx))
                    {
                        delCmd.Parameters.AddWithValue(storeName);
                        delCmd.Parameters.AddWithValue(delOp.Key);
                        await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
