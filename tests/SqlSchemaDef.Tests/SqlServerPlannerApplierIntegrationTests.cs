using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SqlServerPlannerApplierIntegrationTests
{
    private static string? GetMasterConnectionStringOrNull()
    {
        var cs = Environment.GetEnvironmentVariable("SQLSCHEMADEF_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(cs))
        {
            return null;
        }

        var builder = new SqlConnectionStringBuilder(cs);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            builder.InitialCatalog = "master";
        }
        return builder.ConnectionString;
    }

    [Fact]
    public async Task PlanApplyPlan_IsIdempotent()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }
        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var desiredSql = @"
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  Name nvarchar(100) NULL,
  CONSTRAINT PK_Users PRIMARY KEY (Id)
)
CREATE INDEX IX_Users_Name ON dbo.Users (Name)
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task ExistingRows_AddNotNullColumn_IsSkipped()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }
        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (Id int NOT NULL);
INSERT INTO dbo.Users (Id) VALUES (1);
";
            await cmd.ExecuteNonQueryAsync();
        }

        var desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL)
ALTER TABLE dbo.Users ADD Age int NOT NULL
";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());

        Assert.True(plan.IsEmpty);
        Assert.Contains(plan.Skipped, s => s.Reason == SkippedReason.NotNullAddNotSupported);
    }

    [Fact]
    public async Task CurrentHasExtraObjects_NoDropOperations()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Extra (Id int NOT NULL);
CREATE TABLE dbo.Users (Id int NOT NULL);
";
            await cmd.ExecuteNonQueryAsync();
        }

        var desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());

        Assert.True(plan.IsEmpty);
        Assert.Contains(plan.Skipped, s => s.Reason == SkippedReason.DropNotSupported && s.Target.Type == SqlObjectType.Table);
    }

    [Fact]
    public async Task PlanApplyPlan_WithForeignKey_IsIdempotent()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var desiredSql = @"
CREATE TABLE dbo.Teams (Id int NOT NULL, CONSTRAINT PK_Teams PRIMARY KEY (Id))
CREATE TABLE dbo.Users (Id int NOT NULL, TeamId int NOT NULL, CONSTRAINT PK_Users PRIMARY KEY (Id))
ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task PlanApplyPlan_WithAlterAddNullableColumn_IsIdempotent()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL)
ALTER TABLE dbo.Users ADD Nickname nvarchar(50) NULL
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task PlanApplyPlan_WithCheckConstraint_IsIdempotent()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var desiredSql = @"
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  Age int NULL,
  CONSTRAINT CK_Users_Age CHECK (Age > 0)
)
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task PlanApplyPlan_WithUniqueIndex_IsIdempotent()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Email nvarchar(255) NOT NULL)
CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email)
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task MissingConstraintsAndForeignKeys_ConvergeAfterApply()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Teams (Id int NOT NULL, Name nvarchar(100) NOT NULL);
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  TeamId int NOT NULL,
  Email nvarchar(255) NOT NULL,
  Age int NULL
);";
            await cmd.ExecuteNonQueryAsync();
        }

        var desiredSql = @"
CREATE TABLE dbo.Teams (
  Id int NOT NULL,
  Name nvarchar(100) NOT NULL,
  CONSTRAINT PK_Teams PRIMARY KEY (Id),
  CONSTRAINT UQ_Teams_Name UNIQUE (Name)
)
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  TeamId int NOT NULL,
  Email nvarchar(255) NOT NULL,
  Age int NULL,
  CONSTRAINT PK_Users PRIMARY KEY (Id),
  CONSTRAINT UQ_Users_Email UNIQUE (Email),
  CONSTRAINT CK_Users_Age CHECK (Age > 0)
)
ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)
";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn2, plan1, new ApplyOptions());

        var plan2 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task Apply_OnFailure_ThrowsApplyFailedExceptionWithOperation()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Invalid SQL",
                    Sql = "THIS_IS_NOT_VALID_SQL",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "X" },
                },
            },
            Array.Empty<SkippedItem>());

        var applier = new SqlServerSchemaApplier();
        var ex = await Assert.ThrowsAsync<ApplyFailedException>(() =>
            applier.ApplyAsync(conn, plan, new ApplyOptions()));

        Assert.NotNull(ex.Operation);
        Assert.Equal("Invalid SQL", ex.Operation.Description);
    }

    [Fact]
    public async Task Apply_SingleTransaction_RollsBackOnFailure()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Create dbo.Users",
                    Sql = "CREATE TABLE dbo.Users (Id int NOT NULL)",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                },
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Invalid SQL",
                    Sql = "THIS_IS_NOT_VALID_SQL",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "X" },
                },
            },
            Array.Empty<SkippedItem>());

        var applier = new SqlServerSchemaApplier();
        await Assert.ThrowsAsync<ApplyFailedException>(() => applier.ApplyAsync(conn, plan, new ApplyOptions()));

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM sys.tables WHERE name = N'Users'";
        var scalar = await check.ExecuteScalarAsync();
        var count = Convert.ToInt32(scalar);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExportThenPlan_IsEmpty()
    {
        var master = GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(50) NULL);
CREATE INDEX IX_Users_Name ON dbo.Users (Name);
";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var exporter = new SqlServerSchemaExporter();
        var export = await exporter.ExportAsync(conn2, new ExportOptions());

        var planner = new SqlServerSchemaPlanner();
        var plan = await planner.PlanAsync(conn2, export.Script, new PlannerOptions());

        Assert.True(plan.IsEmpty);
    }
}
