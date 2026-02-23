using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SqlSchemaDef.Tests;

[Collection(CliSerialGroup.Name)]
[Trait("Category", "Integration")]
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
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";

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
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";

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

    [Fact]
    public async Task CliExportThenPlan_ProducesEmptyPlan()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        var cs = new SqlConnectionStringBuilder(master) { InitialCatalog = db.DatabaseName }.ConnectionString;

        // Seed a table
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(50) NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        var tempFile = Path.Combine(Path.GetTempPath(), "SqlSchemaDef_" + Guid.NewGuid().ToString("N") + ".sql");
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            // Export
            string exportOutput;
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);

                var exportExitCode = await SqlSchemaDef.Cli.Program.Main(new[]
                {
                    "export", "--connection", cs,
                });

                Console.SetOut(originalOut);
                Console.SetError(originalError);

                Assert.Equal(0, exportExitCode);
                exportOutput = stdout.ToString();
                Assert.Contains("CREATE TABLE", exportOutput);
            }

            // Write export output to temp file
            await File.WriteAllTextAsync(tempFile, exportOutput, Encoding.UTF8);

            // Plan against export = empty
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);

                var planExitCode = await SqlSchemaDef.Cli.Program.Main(new[]
                {
                    "plan", "--connection", cs, "--file", tempFile,
                });

                Console.SetOut(originalOut);
                Console.SetError(originalError);

                Assert.Equal(0, planExitCode);
                Assert.Contains("Operations: 0", stdout.ToString());
            }
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
    public async Task Plan_StrictWithSkippedItems_ReturnsExitCode30()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        var cs = new SqlConnectionStringBuilder(master) { InitialCatalog = db.DatabaseName }.ConnectionString;

        // Seed a table
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Users (Id int NOT NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        // Desired: add NOT NULL column without DEFAULT → skipped → strict fails
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NOT NULL)";
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
                "plan", "--connection", cs, "--file", tempFile, "--strict",
            });

            Assert.Equal(30, exitCode);
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
    public async Task Plan_FormatJson_OutputsValidJson()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        var cs = new SqlConnectionStringBuilder(master) { InitialCatalog = db.DatabaseName }.ConnectionString;

        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";
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
                "plan", "--connection", cs, "--file", tempFile, "--format", "json",
            });

            Assert.Equal(0, exitCode);
            var json = stdout.ToString().Trim();
            Assert.StartsWith("{", json);
            Assert.Contains("\"operations\"", json);
            Assert.Contains("\"metadata\"", json);
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
    public async Task CliApplyThenExport_SchemaReflected()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        var cs = new SqlConnectionStringBuilder(master) { InitialCatalog = db.DatabaseName }.ConnectionString;
        const string desiredSql = "CREATE TABLE dbo.Items (Id int NOT NULL, Name nvarchar(50) NULL)";

        var tempFile = Path.Combine(Path.GetTempPath(), "SqlSchemaDef_" + Guid.NewGuid().ToString("N") + ".sql");
        await File.WriteAllTextAsync(tempFile, desiredSql, Encoding.UTF8);

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            // Apply
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);

                var applyExitCode = await SqlSchemaDef.Cli.Program.Main(new[]
                {
                    "apply", "--connection", cs, "--file", tempFile,
                });

                Console.SetOut(originalOut);
                Console.SetError(originalError);

                Assert.Equal(0, applyExitCode);
            }

            // Export and verify
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);

                var exportExitCode = await SqlSchemaDef.Cli.Program.Main(new[]
                {
                    "export", "--connection", cs,
                });

                Console.SetOut(originalOut);
                Console.SetError(originalError);

                Assert.Equal(0, exportExitCode);
                var exportOutput = stdout.ToString();
                Assert.Contains("Items", exportOutput);
                Assert.Contains("Id", exportOutput);
                Assert.Contains("Name", exportOutput);
            }
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
}
