using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlSchemaDef.Tests;

internal sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private static readonly Lazy<string?> CachedMasterConnectionString =
        new Lazy<string?>(ResolveMasterConnectionString);

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

    internal static string? GetMasterConnectionStringOrNull() => CachedMasterConnectionString.Value;

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

    private static string? ResolveMasterConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("SQLSCHEMADEF_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(cs))
        {
            var builder = new SqlConnectionStringBuilder(cs);
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                builder.InitialCatalog = "master";
            }

            return builder.ConnectionString;
        }

        return TryLocalConnection();
    }

    private static string? TryLocalConnection()
    {
        string[] candidates =
        {
            @"Server=(localdb)\MSSQLLocalDB;Initial Catalog=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5",
            @"Server=.\SQLEXPRESS;Initial Catalog=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5",
        };

        foreach (var cs in candidates)
        {
            try
            {
                using var conn = new SqlConnection(cs);
                conn.Open();
                return cs;
            }
            catch
            {
                // try next candidate
            }
        }

        return null;
    }
}
