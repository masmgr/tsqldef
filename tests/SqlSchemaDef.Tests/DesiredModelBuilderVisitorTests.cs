using System.Linq;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class DesiredModelBuilderVisitorTests
{
    [Fact]
    public void Load_UnsupportedStatement_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedDesiredStatementException>(() =>
            loader.Load("CREATE VIEW dbo.V AS SELECT 1"));

        Assert.Equal(0, ex.BatchIndex);
        Assert.Equal(1, ex.Line);
        Assert.Equal(1, ex.Column);
        Assert.Equal("CreateViewStatement", ex.StatementType);
        Assert.Contains("Unsupported desired statement in v1", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedAlterColumn_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedDesiredStatementException>(() =>
            loader.Load("ALTER TABLE dbo.Users ALTER COLUMN Name int"));

        Assert.Equal("AlterTableAlterColumnStatement", ex.StatementType);
        Assert.Contains("Unsupported desired statement in v1", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedSchema_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedSchemaException>(() =>
            loader.Load("CREATE TABLE foo.Bar (Id int)"));

        Assert.Equal(0, ex.BatchIndex);
        Assert.Equal(1, ex.Line);
        Assert.Equal("foo", ex.SchemaName);
        Assert.Contains("Only schema 'dbo' is supported", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedForeignKeySchema_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedSchemaException>(() =>
            loader.Load("CREATE TABLE dbo.T (Id int, CONSTRAINT FK_T FOREIGN KEY (Id) REFERENCES foo.Ref(Id))"));

        Assert.Equal("foo", ex.SchemaName);
        Assert.Contains("Only schema 'dbo' is supported", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedFeature_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            loader.Load("CREATE INDEX IX_T ON dbo.T (Id) INCLUDE (OtherId)"));

        Assert.Equal(0, ex.BatchIndex);
        Assert.Equal("CreateIndexStatement", ex.StatementType);
        Assert.Equal("IndexInclude", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_FilteredIndex_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            loader.Load("CREATE INDEX IX_T ON dbo.T (Id) WHERE Id > 0"));

        Assert.Equal("IndexFilter", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_IndexOptions_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            loader.Load("CREATE INDEX IX_T ON dbo.T (Id) WITH (ONLINE = ON)"));

        Assert.Equal("IndexOptions", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_ComputedColumn_Throws()
    {
        var loader = new DesiredSchemaLoader();

        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            loader.Load("CREATE TABLE dbo.T (Computed AS (1))"));

        Assert.Equal("ComputedColumn", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_CreateTableAndAlter_AddsToModel()
    {
        var loader = new DesiredSchemaLoader();
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (Id int NOT NULL)",
            "ALTER TABLE dbo.Users ADD Name nvarchar(100) NULL",
            "ALTER TABLE dbo.Users ADD CONSTRAINT PK_Users PRIMARY KEY (Id)",
            "CREATE UNIQUE INDEX IX_Users_Name ON dbo.Users (Name)",
        });

        var model = loader.Load(sql);
        var table = model.Tables.Values.Single();

        Assert.Equal("dbo", table.Schema);
        Assert.Equal("Users", table.Name);
        Assert.True(table.Columns.ContainsKey("ID"));
        Assert.True(table.Columns.ContainsKey("NAME"));
        Assert.True(table.Constraints.ContainsKey("PK_USERS"));
        Assert.True(table.Indexes.ContainsKey("IX_USERS_NAME"));
    }

    [Fact]
    public void Load_AlterTableAddNotNullColumn_IsSkipped()
    {
        var loader = new DesiredSchemaLoader();

        var model = loader.Load(
            "ALTER TABLE dbo.Users ADD Age int NOT NULL",
            new PlannerOptions(),
            out var skipped);

        var table = model.Tables.Values.Single();
        Assert.False(table.Columns.ContainsKey("AGE"));

        var item = Assert.Single(skipped);
        Assert.Equal(SkippedReason.NotNullAddNotSupported, item.Reason);
        Assert.Equal("dbo.Users.Age", item.Target.ToDisplayName());
    }
}
