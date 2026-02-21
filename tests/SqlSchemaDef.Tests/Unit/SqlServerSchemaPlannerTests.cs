using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class SqlServerSchemaPlannerTests
{
    [Fact]
    public async Task PlanAsync_WithNonDboSchema_PassesSchemaValidation()
    {
        await using var connection = new SqlConnection();
        var planner = new SqlServerSchemaPlanner();

        // スキーマ検証は通過し、接続エラーが発生することを確認（UnsupportedSchemaException ではない）
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            planner.PlanAsync(
                connection,
                "CREATE TABLE sales.T (Id int NOT NULL)",
                new PlannerOptions { Schema = "sales" }));

        Assert.IsNotType<UnsupportedSchemaException>(ex);
    }

    [Fact]
    public async Task ExportAsync_WithNonDboSchema_PassesSchemaValidation()
    {
        await using var connection = new SqlConnection();

        // スキーマ検証は通過し、接続エラーが発生することを確認（UnsupportedSchemaException ではない）
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            SqlServerSchemaExporter.ExportAsync(
                connection,
                new ExportOptions { Schema = "sales" }));

        Assert.IsNotType<UnsupportedSchemaException>(ex);
    }
}
