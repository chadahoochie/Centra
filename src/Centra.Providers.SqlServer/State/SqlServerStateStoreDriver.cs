using System.Data;
using Centra.Drivers;
using Centra.Providers.SqlServer.Options;
using Centra.State;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Centra.Providers.SqlServer.State;

public sealed class SqlServerStateStoreDriver : IStateStoreDriver
{
    private readonly SqlServerProviderOptions _options;
    private readonly string _fullTableName;
    private int _initialized;

    public SqlServerStateStoreDriver(IOptions<SqlServerProviderOptions> options)
    {
        _options = options?.Value ?? new SqlServerProviderOptions();
        _fullTableName = $"[{_options.SchemaName}].[{_options.StateTableName}]";
    }

    internal async ValueTask EnsureTableCreatedAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoCreateTable && Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            var sql = $"""
                IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = @tbl AND s.name = @sch)
                BEGIN
                    CREATE TABLE {_fullTableName} (
                        [store_name] NVARCHAR(128) NOT NULL,
                        [key] NVARCHAR(256) NOT NULL,
                        [value] VARBINARY(MAX) NOT NULL,
                        [etag] NVARCHAR(64) NOT NULL,
                        [metadata] NVARCHAR(MAX) NULL,
                        [expire_at_utc] DATETIMEOFFSET NULL,
                        [updated_at_utc] DATETIMEOFFSET NOT NULL,
                        CONSTRAINT [PK_{_options.StateTableName}] PRIMARY KEY CLUSTERED ([store_name], [key])
                    );
                    CREATE NONCLUSTERED INDEX [IX_{_options.StateTableName}_expire] ON {_fullTableName} ([expire_at_utc]) WHERE [expire_at_utc] IS NOT NULL;
                END
                """;

            await using var conn = new SqlConnection(_options.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@tbl", SqlDbType.NVarChar, 128) { Value = _options.StateTableName });
            cmd.Parameters.Add(new SqlParameter("@sch", SqlDbType.NVarChar, 128) { Value = _options.SchemaName });
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
            SELECT [value], [etag] FROM {_fullTableName}
            WHERE [store_name] = @store_name AND [key] = @key AND ([expire_at_utc] IS NULL OR [expire_at_utc] > @now);
            """;

        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
        cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = key });
        cmd.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTimeOffset) { Value = now });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var value = (byte[])reader[0];
            var etag = reader.GetString(1);
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
            MERGE {_fullTableName} WITH (HOLDLOCK) AS target
            USING (SELECT @store_name AS store_name, @key AS [key]) AS source
            ON (target.store_name = source.store_name AND target.[key] = source.[key])
            WHEN MATCHED THEN
                UPDATE SET [value] = @value, [etag] = @etag, [metadata] = @metadata, [expire_at_utc] = @expire_at_utc, [updated_at_utc] = @updated_at_utc
            WHEN NOT MATCHED THEN
                INSERT (store_name, [key], [value], [etag], [metadata], [expire_at_utc], [updated_at_utc])
                VALUES (@store_name, @key, @value, @etag, @metadata, @expire_at_utc, @updated_at_utc);
            """;

        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
        cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = key });
        cmd.Parameters.Add(new SqlParameter("@value", SqlDbType.VarBinary, -1) { Value = value.ToArray() });
        cmd.Parameters.Add(new SqlParameter("@etag", SqlDbType.NVarChar, 64) { Value = newEtag });
        cmd.Parameters.Add(new SqlParameter("@metadata", SqlDbType.NVarChar, -1) { Value = DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@expire_at_utc", SqlDbType.DateTimeOffset) { Value = expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@updated_at_utc", SqlDbType.DateTimeOffset) { Value = now });

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
                [value] = @value,
                [etag] = @etag,
                [expire_at_utc] = @expire_at_utc,
                [updated_at_utc] = @updated_at_utc
            WHERE [store_name] = @store_name AND [key] = @key AND [etag] = @expected_etag;
            """;

        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@value", SqlDbType.VarBinary, -1) { Value = value.ToArray() });
        cmd.Parameters.Add(new SqlParameter("@etag", SqlDbType.NVarChar, 64) { Value = newEtag });
        cmd.Parameters.Add(new SqlParameter("@expire_at_utc", SqlDbType.DateTimeOffset) { Value = expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@updated_at_utc", SqlDbType.DateTimeOffset) { Value = now });
        cmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
        cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = key });
        cmd.Parameters.Add(new SqlParameter("@expected_etag", SqlDbType.NVarChar, 64) { Value = expectedETag });

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
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

        var sql = $"DELETE FROM {_fullTableName} WHERE [store_name] = @store_name AND [key] = @key;";
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
        cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = key });

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

        var sql = $"DELETE FROM {_fullTableName} WHERE [store_name] = @store_name AND [key] = @key AND [etag] = @expected_etag;";
        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
        cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = key });
        cmd.Parameters.Add(new SqlParameter("@expected_etag", SqlDbType.NVarChar, 64) { Value = expectedETag });

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
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

        await using var conn = new SqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = conn.BeginTransaction();

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
                        MERGE {_fullTableName} WITH (HOLDLOCK) AS target
                        USING (SELECT @store_name AS store_name, @key AS [key]) AS source
                        ON (target.store_name = source.store_name AND target.[key] = source.[key])
                        WHEN MATCHED THEN
                            UPDATE SET [value] = @value, [etag] = @etag, [metadata] = @metadata, [expire_at_utc] = @expire_at_utc, [updated_at_utc] = @updated_at_utc
                        WHEN NOT MATCHED THEN
                            INSERT (store_name, [key], [value], [etag], [metadata], [expire_at_utc], [updated_at_utc])
                            VALUES (@store_name, @key, @value, @etag, @metadata, @expire_at_utc, @updated_at_utc);
                        """;

                    await using (var setCmd = new SqlCommand(setSql, conn, tx))
                    {
                        setCmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
                        setCmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = setOp.Key });
                        setCmd.Parameters.Add(new SqlParameter("@value", SqlDbType.VarBinary, -1) { Value = setOp.Value });
                        setCmd.Parameters.Add(new SqlParameter("@etag", SqlDbType.NVarChar, 64) { Value = newEtag });
                        setCmd.Parameters.Add(new SqlParameter("@metadata", SqlDbType.NVarChar, -1) { Value = DBNull.Value });
                        setCmd.Parameters.Add(new SqlParameter("@expire_at_utc", SqlDbType.DateTimeOffset) { Value = expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value });
                        setCmd.Parameters.Add(new SqlParameter("@updated_at_utc", SqlDbType.DateTimeOffset) { Value = now });
                        await setCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;

                case DeleteTransactionOperation delOp:
                    var delSql = $"DELETE FROM {_fullTableName} WHERE [store_name] = @store_name AND [key] = @key;";
                    await using (var delCmd = new SqlCommand(delSql, conn, tx))
                    {
                        delCmd.Parameters.Add(new SqlParameter("@store_name", SqlDbType.NVarChar, 128) { Value = storeName });
                        delCmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 256) { Value = delOp.Key });
                        await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
