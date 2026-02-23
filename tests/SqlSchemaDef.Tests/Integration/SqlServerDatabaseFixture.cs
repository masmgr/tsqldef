using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    private SqlServerTestDatabase? _database;

    public bool IsAvailable => _database != null;

    public string ConnectionString
    {
        get
        {
            if (_database == null)
            {
                throw new InvalidOperationException("Test database is not available.");
            }

            return _database.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        _database = await SqlServerTestDatabase.CreateAsync(master).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_database == null)
        {
            return;
        }

        await _database.DisposeAsync().ConfigureAwait(false);
        _database = null;
    }

    public async Task<string?> GetPreparedConnectionStringOrNullAsync()
    {
        if (_database == null)
        {
            return null;
        }

        await ResetAsync().ConfigureAwait(false);
        return _database.ConnectionString;
    }

    private async Task ResetAsync()
    {
        if (_database == null)
        {
            return;
        }

        await using var conn = new SqlConnection(_database.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DECLARE @dropFkSql nvarchar(max) = N'';
SELECT @dropFkSql = @dropFkSql
    + N'ALTER TABLE '
    + QUOTENAME(SCHEMA_NAME(t.schema_id))
    + N'.'
    + QUOTENAME(t.name)
    + N' DROP CONSTRAINT '
    + QUOTENAME(fk.name)
    + N';'
FROM sys.foreign_keys fk
JOIN sys.tables t ON t.object_id = fk.parent_object_id
WHERE t.is_ms_shipped = 0;

IF LEN(@dropFkSql) > 0 EXEC sp_executesql @dropFkSql;

DECLARE @dropViewSql nvarchar(max) = N'';
SELECT @dropViewSql = @dropViewSql
    + N'DROP VIEW '
    + QUOTENAME(SCHEMA_NAME(v.schema_id))
    + N'.'
    + QUOTENAME(v.name)
    + N';'
FROM sys.views v
WHERE v.is_ms_shipped = 0;

IF LEN(@dropViewSql) > 0 EXEC sp_executesql @dropViewSql;

DECLARE @dropTableSql nvarchar(max) = N'';
SELECT @dropTableSql = @dropTableSql
    + N'DROP TABLE '
    + QUOTENAME(SCHEMA_NAME(t.schema_id))
    + N'.'
    + QUOTENAME(t.name)
    + N';'
FROM sys.tables t
WHERE t.is_ms_shipped = 0;

IF LEN(@dropTableSql) > 0 EXEC sp_executesql @dropTableSql;
";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
