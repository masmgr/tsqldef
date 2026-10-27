using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

[Trait("Category", "Integration")]
public sealed class ReviewRegressionIntegrationTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _fixture;

    public ReviewRegressionIntegrationTests(SqlServerDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task DefaultApply_PreservesOmittedColumnData()
    {
        return WithDatabaseAsync(async connection =>
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL, Secret nvarchar(20) NULL); INSERT dbo.T VALUES (1,N'keep')");
            var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL)");
            Assert.Empty(plan.Operations);
            Assert.Contains(plan.Skipped, item => item.Reason == SkippedReason.DropNotSupported);
            await new SqlServerSchemaApplier().ApplyAsync(connection, plan);
            Assert.Equal("keep", await ScalarAsync(connection, "SELECT Secret FROM dbo.T WHERE Id=1"));
        });
    }

    [Fact]
    public Task SavedJsonPlan_RequiresPermissionAgainAtApply()
    {
        return WithDatabaseAsync(async connection =>
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL, Secret int NULL)");
            var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL)", new PlannerOptions { AllowDrop = true });
            using var file = await CliTestHelper.CreateTempSqlFileAsync(MigrationPlanSerializer.ToJson(plan));
            var rejected = await CliTestHelper.RunAsync("apply", "--connection", connection.ConnectionString, "--plan", file.Path);
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("AllowDrop", rejected.StdErr);
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.T') AND name='Secret'"));
            var accepted = await CliTestHelper.RunAsync("apply", "--connection", connection.ConnectionString, "--plan", file.Path, "--allow-drop");
            Assert.Equal(0, accepted.ExitCode);
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.T') AND name='Secret'"));
        });
    }

    [Fact]
    public Task ColumnLevelConstraints_ExistAndEnforceIntegrity()
    {
        return WithDatabaseAsync(async connection =>
        {
            const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL CONSTRAINT PK_Parent PRIMARY KEY); "
                + "CREATE TABLE dbo.Child (Id int NOT NULL CONSTRAINT UQ_Child UNIQUE, Age int NULL CONSTRAINT CK_Child CHECK(Age >= 0), "
                + "ParentId int NULL CONSTRAINT FK_Child REFERENCES dbo.Parent(Id))";
            await ApplyDesiredAsync(connection, desired);
            Assert.Equal(2, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.key_constraints WHERE name IN ('PK_Parent','UQ_Child')"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.check_constraints WHERE name='CK_Child'"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_Child'"));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "INSERT dbo.Child VALUES (1,-1,NULL)"));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "INSERT dbo.Child VALUES (1,1,123)"));
            Assert.True((await new SqlServerSchemaPlanner().PlanAsync(connection, desired)).IsEmpty);
        });
    }

    [Fact]
    public Task IdentityParameters_ControlInsertedValuesAndRoundTrip()
    {
        return WithDatabaseAsync(async connection =>
        {
            const string desired = "CREATE TABLE dbo.T (Id bigint IDENTITY(1000,10) NOT NULL)";
            await ApplyDesiredAsync(connection, desired);
            Assert.Equal(1000L, await ScalarAsync(connection, "INSERT dbo.T OUTPUT inserted.Id DEFAULT VALUES"));
            Assert.Equal(1010L, await ScalarAsync(connection, "INSERT dbo.T OUTPUT inserted.Id DEFAULT VALUES"));
            var export = await SqlServerSchemaExporter.ExportAsync(connection);
            Assert.Contains("IDENTITY(1000,10)", export.Script);
            var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, export.Script);
            Assert.Empty(plan.Operations);
            Assert.Empty(plan.Skipped);
        });
    }

    [Fact]
    public Task ClusteredIndexReplacement_AppliesAndConverges()
    {
        return WithDatabaseAsync(async connection =>
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); CREATE CLUSTERED INDEX IX_Old ON dbo.T(Id)");
            const string desired = "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); CREATE CLUSTERED INDEX IX_New ON dbo.T(Val)";
            var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, desired, new PlannerOptions { AllowDrop = true });
            Assert.Single(plan.Operations);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlServerSchemaApplier().ApplyAsync(connection, plan));
            await new SqlServerSchemaApplier().ApplyAsync(connection, plan, new ApplyOptions { AllowDrop = true });
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.T') AND name='IX_Old'"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.T') AND name='IX_New' AND type=1"));
            Assert.True((await new SqlServerSchemaPlanner().PlanAsync(connection, desired)).IsEmpty);
        });
    }

    [Fact]
    public Task RebuildWithDefault_PreservesRowsAndDefaultBehavior()
    {
        return WithDatabaseAsync(async connection =>
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL, Val varchar(20) NULL CONSTRAINT DF_T_Val DEFAULT '7'); INSERT dbo.T VALUES(1,'12')");
            const string desired = "CREATE TABLE dbo.T (Id int NOT NULL, Val int NULL CONSTRAINT DF_T_Val DEFAULT 7)";
            var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, desired, new PlannerOptions { EmitProposals = true });
            Assert.Single(plan.Proposals);
            await new SqlServerSchemaApplier().ApplyAsync(connection, plan, new ApplyOptions { ApplyProposals = true });
            Assert.Equal(12, await ScalarAsync(connection, "SELECT Val FROM dbo.T WHERE Id=1"));
            await ExecuteAsync(connection, "INSERT dbo.T(Id) VALUES(2)");
            Assert.Equal(7, await ScalarAsync(connection, "SELECT Val FROM dbo.T WHERE Id=2"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID('dbo.T')"));
            var next = await new SqlServerSchemaPlanner().PlanAsync(connection, desired);
            Assert.Empty(next.Operations);
            Assert.Empty(next.Skipped);
        });
    }

    [Fact]
    public Task PrimaryKeyAndForeignKeyChange_UsesNewCascadeBehavior()
    {
        return WithDatabaseAsync(async connection =>
        {
            const string initialSql = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
                + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id)); INSERT dbo.Parent VALUES(1); INSERT dbo.Child VALUES(1)";
            await ExecuteAsync(connection, initialSql);
            const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED(Id)); "
                + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id) ON DELETE CASCADE)";
            await ApplyDesiredAsync(connection, desired, true);
            Assert.Equal(1, await ScalarAsync(connection, "SELECT CONVERT(int,delete_referential_action) FROM sys.foreign_keys WHERE name='FK_Child'"));
            await ExecuteAsync(connection, "DELETE dbo.Parent WHERE Id=1");
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM dbo.Child"));
            var next = await new SqlServerSchemaPlanner().PlanAsync(connection, desired);
            Assert.Empty(next.Operations);
            Assert.Empty(next.Skipped);
        });
    }

    [Fact]
    public Task CheckLiteralCaseChange_ChangesAllowedValues()
    {
        return WithDatabaseAsync(async connection =>
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.T (Val varchar(10) COLLATE Latin1_General_100_CS_AS NOT NULL, CONSTRAINT CK_T CHECK(Val='A'))");
            const string desired = "CREATE TABLE dbo.T (Val varchar(10) COLLATE Latin1_General_100_CS_AS NOT NULL, CONSTRAINT CK_T CHECK(Val='a'))";
            await ApplyDesiredAsync(connection, desired, true);
            await ExecuteAsync(connection, "INSERT dbo.T VALUES('a')");
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "INSERT dbo.T VALUES('A')"));
            var next = await new SqlServerSchemaPlanner().PlanAsync(connection, desired);
            Assert.Empty(next.Operations);
            Assert.Empty(next.Skipped);
        });
    }

    [Fact]
    public Task ClusteredIndexReplacement_WithoutPermission_KeepsOriginalIndex()
    {
        return WithDatabaseAsync(async connection =>
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); CREATE CLUSTERED INDEX IX_Old ON dbo.T(Id)");
            const string desired = "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); "
                + "CREATE NONCLUSTERED INDEX IX_Old ON dbo.T(Id); CREATE CLUSTERED INDEX IX_New ON dbo.T(Val)";
            var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, desired);
            Assert.Empty(plan.Operations);
            await new SqlServerSchemaApplier().ApplyAsync(connection, plan);
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.T') AND name='IX_Old' AND type=1"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.T') AND name='IX_New'"));
            await ApplyDesiredAsync(connection, desired, true);
            var next = await new SqlServerSchemaPlanner().PlanAsync(connection, desired);
            Assert.Empty(next.Operations);
            Assert.Empty(next.Skipped);
        });
    }

    [Fact]
    public Task PrimaryKeyRecreation_ForeignKeyCanTargetNewTable()
    {
        return WithDatabaseAsync(async connection =>
        {
            const string initial = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
                + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id))";
            await ExecuteAsync(connection, initial);
            const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED(Id)); "
                + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.ZNewParent(Id)); "
                + "CREATE TABLE dbo.ZNewParent (Id int NOT NULL, CONSTRAINT PK_NewParent PRIMARY KEY(Id))";
            await ApplyDesiredAsync(connection, desired, true);
            await ExecuteAsync(connection, "INSERT dbo.ZNewParent VALUES(1); INSERT dbo.Child VALUES(1)");
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_Child' AND referenced_object_id=OBJECT_ID('dbo.ZNewParent')"));
            var next = await new SqlServerSchemaPlanner().PlanAsync(connection, desired);
            Assert.Empty(next.Operations);
            Assert.Empty(next.Skipped);
        });
    }

    private static async Task ApplyDesiredAsync(SqlConnection connection, string desired, bool allowDrop = false)
    {
        var plan = await new SqlServerSchemaPlanner().PlanAsync(connection, desired, new PlannerOptions { AllowDrop = allowDrop });
        await new SqlServerSchemaApplier().ApplyAsync(connection, plan, new ApplyOptions { AllowDrop = allowDrop });
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private async Task WithDatabaseAsync(Func<SqlConnection, Task> action)
    {
        var connectionString = await _fixture.GetPreparedConnectionStringOrNullAsync();
        if (connectionString == null)
        {
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await action(connection);
    }
}
