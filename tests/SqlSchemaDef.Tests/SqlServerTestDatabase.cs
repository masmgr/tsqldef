using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlSchemaDef.Tests;

internal sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly string _masterConnectionString;

    private SqlServerTestDatabase(string masterConnectionString, string databaseName)
    {
        _masterConnectionString = masterConnectionString;
        _databaseName = databaseName;
    }

    public string DatabaseName => _databaseName;

    public string ConnectionString
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(_masterConnectionString)
            {
                InitialCatalog = _databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public static async Task<SqlServerTestDatabase> CreateAsync(string masterConnectionString)
    {
        if (string.IsNullOrWhiteSpace(masterConnectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(masterConnectionString));
        }

        var name = "SqlSchemaDef_Test_" + Guid.NewGuid().ToString("N");
        var db = new SqlServerTestDatabase(masterConnectionString, name);
        await db.CreateDatabaseAsync().ConfigureAwait(false);
        return db;
    }

    private async Task CreateDatabaseAsync()
    {
        await using var conn = new SqlConnection(_masterConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"CREATE DATABASE [{_databaseName}]";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await using var conn = new SqlConnection(_masterConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                $"IF DB_ID(N'{_databaseName}') IS NOT NULL " +
                $"BEGIN " +
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]; " +
                $"END";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}

