using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class SqlServerSchemaPlannerTests
{
    [Fact]
    public async Task PlanAsync_NullConnection_ThrowsArgumentNullException()
    {
        var planner = new SqlServerSchemaPlanner();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            planner.PlanAsync(null!, "CREATE TABLE dbo.T (Id int NOT NULL)", new PlannerOptions()));
    }

    [Fact]
    public async Task PlanAsync_NullDesiredSql_ThrowsArgumentNullException()
    {
        await using var connection = new SqlConnection();
        var planner = new SqlServerSchemaPlanner();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            planner.PlanAsync(connection, null!, new PlannerOptions()));
    }

    [Fact]
    public async Task PlanAsync_NonSqlConnection_ThrowsArgumentException()
    {
        await using var connection = new FakeDbConnection();
        var planner = new SqlServerSchemaPlanner();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            planner.PlanAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL)", new PlannerOptions()));

        Assert.Contains("SqlServerSchemaPlanner requires Microsoft.Data.SqlClient.SqlConnection.", ex.Message);
    }

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

    [Fact]
    public async Task ExportAsync_NullConnection_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            SqlServerSchemaExporter.ExportAsync(null!, new ExportOptions()));
    }

    [Fact]
    public async Task ExportAsync_NonSqlConnection_ThrowsArgumentException()
    {
        await using var connection = new FakeDbConnection();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            SqlServerSchemaExporter.ExportAsync(connection, new ExportOptions()));

        Assert.Contains("SqlServerSchemaExporter requires Microsoft.Data.SqlClient.SqlConnection.", ex.Message);
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private string _connectionString = string.Empty;

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
        }

        public override void Open()
        {
            throw new NotImplementedException();
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotImplementedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotImplementedException();
        }
    }
}
