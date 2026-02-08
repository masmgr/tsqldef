using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SqlSchemaDef.Tests;

[Collection(SqlServerIntegrationGroup.Name)]
public sealed class CliSmokeTests
{
    [Fact]
    public async Task Help_ReturnsZero()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[] { "--help" });
            Assert.Equal(0, exitCode);
            Assert.Contains("Usage:", stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task DryRun_PrintsPlanAndReturnsZero()
    {
        var master = Environment.GetEnvironmentVariable("SQLSCHEMADEF_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        var desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";

        var tempFile = Path.Combine(Path.GetTempPath(), "SqlSchemaDef_" + Guid.NewGuid().ToString("N") + ".sql");
        await File.WriteAllTextAsync(tempFile, desiredSql, Encoding.UTF8);

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "plan",
                "--connection", new SqlConnectionStringBuilder(master) { InitialCatalog = db.DatabaseName }.ConnectionString,
                "--file", tempFile,
            });

            Assert.Equal(0, exitCode);
            Assert.Contains("CREATE TABLE", stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            try
            { File.Delete(tempFile); }
            catch { }
        }
    }

    [Fact]
    public async Task Apply_CreatesObjectsAndReturnsZero()
    {
        var master = Environment.GetEnvironmentVariable("SQLSCHEMADEF_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        var desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";

        var tempFile = Path.Combine(Path.GetTempPath(), "SqlSchemaDef_" + Guid.NewGuid().ToString("N") + ".sql");
        await File.WriteAllTextAsync(tempFile, desiredSql, Encoding.UTF8);

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "apply",
                "--connection", new SqlConnectionStringBuilder(master) { InitialCatalog = db.DatabaseName }.ConnectionString,
                "--file", tempFile,
            });

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            try
            { File.Delete(tempFile); }
            catch { }
        }

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = N'Users' AND schema_id = SCHEMA_ID(N'dbo')";
        var result = await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
        var count = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        Assert.Equal(1, count);
    }
}
