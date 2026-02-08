using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class SqlServerSchemaPlannerTests
{
    [Fact]
    public async Task PlanAsync_WhenOptionsSchemaIsNotDbo_ThrowsUnsupportedSchemaException()
    {
        await using var connection = new SqlConnection();
        var planner = new SqlServerSchemaPlanner();

        var ex = await Assert.ThrowsAsync<UnsupportedSchemaException>(() =>
            planner.PlanAsync(
                connection,
                "CREATE TABLE dbo.Users (Id int NOT NULL)",
                new PlannerOptions { Schema = "foo" }));

        Assert.Equal("foo", ex.SchemaName);
        Assert.Contains("Only schema 'dbo' is supported", ex.Message);
    }

    [Fact]
    public async Task ExportAsync_WhenOptionsSchemaIsNotDbo_ThrowsUnsupportedSchemaException()
    {
        await using var connection = new SqlConnection();

        var ex = await Assert.ThrowsAsync<UnsupportedSchemaException>(() =>
            SqlServerSchemaExporter.ExportAsync(
                connection,
                new ExportOptions { Schema = "foo" }));

        Assert.Equal("foo", ex.SchemaName);
        Assert.Contains("Only schema 'dbo' is supported", ex.Message);
    }
}
