using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SqlSchemaDef.Tests;

[Collection(CliSerialGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CliSmokeTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _dbFixture;

    public CliSmokeTests(SqlServerDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
    }

    [Fact]
    public async Task Help_ReturnsZero()
    {
        var result = await CliTestHelper.RunAsync("--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.StdErr);
    }

    [Fact]
    public async Task DryRun_PrintsPlanAndReturnsZero()
    {
        var connectionString = await _dbFixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";
        using var tempFile = await CliTestHelper.CreateTempSqlFileAsync(desiredSql);

        var result = await CliTestHelper.RunAsync(
            "plan",
            "--connection",
            connectionString,
            "--file",
            tempFile.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CREATE TABLE", result.StdOut);
    }

    [Fact]
    public async Task Apply_CreatesObjectsAndReturnsZero()
    {
        var connectionString = await _dbFixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";
        using var tempFile = await CliTestHelper.CreateTempSqlFileAsync(desiredSql);

        var result = await CliTestHelper.RunAsync(
            "apply",
            "--connection",
            connectionString,
            "--file",
            tempFile.Path);

        Assert.Equal(0, result.ExitCode);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = N'Users' AND schema_id = SCHEMA_ID(N'dbo')";
        var scalar = await cmd.ExecuteScalarAsync();
        var count = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CliExportThenPlan_ProducesEmptyPlan()
    {
        var connectionString = await _dbFixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(50) NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        var export = await CliTestHelper.RunAsync("export", "--connection", connectionString);
        Assert.Equal(0, export.ExitCode);
        Assert.Contains("CREATE TABLE", export.StdOut);

        using var tempFile = await CliTestHelper.CreateTempSqlFileAsync(export.StdOut);
        var plan = await CliTestHelper.RunAsync("plan", "--connection", connectionString, "--file", tempFile.Path);

        Assert.Equal(0, plan.ExitCode);
        Assert.Contains("Operations: 0", plan.StdOut);
    }

    [Fact]
    public async Task Plan_StrictWithSkippedItems_ReturnsExitCode30()
    {
        var connectionString = await _dbFixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Users (Id int NOT NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NOT NULL)";
        using var tempFile = await CliTestHelper.CreateTempSqlFileAsync(desiredSql);

        var result = await CliTestHelper.RunAsync(
            "plan",
            "--connection",
            connectionString,
            "--file",
            tempFile.Path,
            "--strict");

        Assert.Equal(30, result.ExitCode);
    }

    [Fact]
    public async Task Plan_FormatJson_OutputsValidJson()
    {
        var connectionString = await _dbFixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";
        using var tempFile = await CliTestHelper.CreateTempSqlFileAsync(desiredSql);

        var result = await CliTestHelper.RunAsync(
            "plan",
            "--connection",
            connectionString,
            "--file",
            tempFile.Path,
            "--format",
            "json");

        Assert.Equal(0, result.ExitCode);
        var json = result.StdOut.Trim();
        Assert.StartsWith("{", json);
        Assert.Contains("\"operations\"", json);
        Assert.Contains("\"metadata\"", json);
    }

    [Fact]
    public async Task CliApplyThenExport_SchemaReflected()
    {
        var connectionString = await _dbFixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        const string desiredSql = "CREATE TABLE dbo.Items (Id int NOT NULL, Name nvarchar(50) NULL)";
        using var tempFile = await CliTestHelper.CreateTempSqlFileAsync(desiredSql);

        var apply = await CliTestHelper.RunAsync(
            "apply",
            "--connection",
            connectionString,
            "--file",
            tempFile.Path);

        Assert.Equal(0, apply.ExitCode);

        var export = await CliTestHelper.RunAsync("export", "--connection", connectionString);
        Assert.Equal(0, export.ExitCode);
        Assert.Contains("Items", export.StdOut);
        Assert.Contains("Id", export.StdOut);
        Assert.Contains("Name", export.StdOut);
    }
}
