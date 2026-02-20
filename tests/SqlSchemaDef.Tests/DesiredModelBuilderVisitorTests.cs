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
        var ex = Assert.Throws<UnsupportedDesiredStatementException>(() =>
            DesiredSchemaLoader.Load("CREATE VIEW dbo.V AS SELECT 1"));

        Assert.Equal(0, ex.BatchIndex);
        Assert.Equal(1, ex.Line);
        Assert.Equal(1, ex.Column);
        Assert.Equal("CreateViewStatement", ex.StatementType);
        Assert.Contains("Unsupported desired statement in v1", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedAlterColumn_Throws()
    {
        var ex = Assert.Throws<UnsupportedDesiredStatementException>(() =>
            DesiredSchemaLoader.Load("ALTER TABLE dbo.Users ALTER COLUMN Name int"));

        Assert.Equal("AlterTableAlterColumnStatement", ex.StatementType);
        Assert.Contains("Unsupported desired statement in v1", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedSchema_Throws()
    {
        var ex = Assert.Throws<UnsupportedSchemaException>(() =>
            DesiredSchemaLoader.Load("CREATE TABLE foo.Bar (Id int)"));

        Assert.Equal(0, ex.BatchIndex);
        Assert.Equal(1, ex.Line);
        Assert.Equal("foo", ex.SchemaName);
        Assert.Contains("Only schema 'dbo' is supported", ex.Message);
    }

    [Fact]
    public void Load_UnsupportedForeignKeySchema_Throws()
    {
        var ex = Assert.Throws<UnsupportedSchemaException>(() =>
            DesiredSchemaLoader.Load("CREATE TABLE dbo.T (Id int, CONSTRAINT FK_T FOREIGN KEY (Id) REFERENCES foo.Ref(Id))"));

        Assert.Equal("foo", ex.SchemaName);
        Assert.Contains("Only schema 'dbo' is supported", ex.Message);
    }

    [Fact]
    public void Load_IndexIncludeAndSortOrder_AreMapped()
    {
        var model = DesiredSchemaLoader.Load("CREATE INDEX IX_T ON dbo.T (Id DESC, Name) INCLUDE (OtherId)");

        var table = model.Tables.Values.Single();
        var index = table.Indexes.Values.Single();

        Assert.Equal("IX_T", index.Name);
        Assert.Equal(2, index.KeyColumns.Count);
        Assert.Equal("Id", index.KeyColumns[0].Name);
        Assert.True(index.KeyColumns[0].IsDescending);
        Assert.Equal("Name", index.KeyColumns[1].Name);
        Assert.False(index.KeyColumns[1].IsDescending);
        Assert.Equal("OtherId", Assert.Single(index.IncludeColumns));
    }

    [Fact]
    public void Load_FilteredIndex_Throws()
    {
        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            DesiredSchemaLoader.Load("CREATE INDEX IX_T ON dbo.T (Id) WHERE Id > 0"));

        Assert.Equal("IndexFilter", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_IndexOptions_Throws()
    {
        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            DesiredSchemaLoader.Load("CREATE INDEX IX_T ON dbo.T (Id) WITH (ONLINE = ON)"));

        Assert.Equal("IndexOptions", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_ComputedColumn_Throws()
    {
        var ex = Assert.Throws<UnsupportedDesiredFeatureException>(() =>
            DesiredSchemaLoader.Load("CREATE TABLE dbo.T (Computed AS (1))"));

        Assert.Equal("ComputedColumn", ex.FeatureName);
        Assert.Contains("Unsupported desired feature in v1", ex.Message);
    }

    [Fact]
    public void Load_CreateTableAndAlter_AddsToModel()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (Id int NOT NULL)",
            "ALTER TABLE dbo.Users ADD Name nvarchar(100) NULL",
            "ALTER TABLE dbo.Users ADD CONSTRAINT PK_Users PRIMARY KEY (Id)",
            "CREATE UNIQUE INDEX IX_Users_Name ON dbo.Users (Name)",
        });

        var model = DesiredSchemaLoader.Load(sql);
        var table = model.Tables.Values.Single();

        Assert.Equal("dbo", table.Schema);
        Assert.Equal("Users", table.Name);
        Assert.True(table.Columns.ContainsKey("ID"));
        Assert.True(table.Columns.ContainsKey("NAME"));
        Assert.True(table.Constraints.ContainsKey("PK_USERS"));
        Assert.True(table.Indexes.ContainsKey("IX_USERS_NAME"));
    }

    [Fact]
    public void Load_ForeignKeyWithOnDeleteCascade_StoresDeleteAction()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Teams (Id int NOT NULL)",
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  TeamId int NOT NULL,",
            "  CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id) ON DELETE CASCADE",
            ")",
        });

        var model = DesiredSchemaLoader.Load(sql);
        var constraint = model.Tables["DBO.USERS"].Constraints["FK_USERS_TEAMS"];

        Assert.Equal(ConstraintKind.ForeignKey, constraint.Kind);
        Assert.Equal("CASCADE", constraint.DeleteAction);
        Assert.Null(constraint.UpdateAction);
    }

    [Fact]
    public void Load_ForeignKeyWithOnUpdateSetNull_StoresUpdateAction()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Teams (Id int NOT NULL)",
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  TeamId int NOT NULL,",
            "  CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id) ON UPDATE SET NULL",
            ")",
        });

        var model = DesiredSchemaLoader.Load(sql);
        var constraint = model.Tables["DBO.USERS"].Constraints["FK_USERS_TEAMS"];

        Assert.Equal("SET NULL", constraint.UpdateAction);
        Assert.Null(constraint.DeleteAction);
    }

    [Fact]
    public void Load_ForeignKeyWithBothActions_StoresBothActions()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Teams (Id int NOT NULL)",
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  TeamId int NOT NULL,",
            "  CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id) ON DELETE CASCADE ON UPDATE SET DEFAULT",
            ")",
        });

        var model = DesiredSchemaLoader.Load(sql);
        var constraint = model.Tables["DBO.USERS"].Constraints["FK_USERS_TEAMS"];

        Assert.Equal("CASCADE", constraint.DeleteAction);
        Assert.Equal("SET DEFAULT", constraint.UpdateAction);
    }

    [Fact]
    public void Load_ForeignKeyNoAction_ActionsAreNull()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Teams (Id int NOT NULL)",
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  TeamId int NOT NULL,",
            "  CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)",
            ")",
        });

        var model = DesiredSchemaLoader.Load(sql);
        var constraint = model.Tables["DBO.USERS"].Constraints["FK_USERS_TEAMS"];

        Assert.Null(constraint.DeleteAction);
        Assert.Null(constraint.UpdateAction);
    }

    [Fact]
    public void Load_InlineDefaultNoName_CreatesAutoNamedDefaultConstraint()
    {
        var model = DesiredSchemaLoader.Load("CREATE TABLE dbo.Users (Id int NOT NULL, Score int DEFAULT (0) NULL)");
        var table = model.Tables.Values.Single();

        Assert.True(table.Constraints.ContainsKey("DF_USERS_SCORE"));
        var constraint = table.Constraints["DF_USERS_SCORE"];
        Assert.Equal(ConstraintKind.Default, constraint.Kind);
        Assert.Equal("DF_Users_Score", constraint.Name);
        Assert.Equal("(0)", constraint.Definition);
        Assert.Equal("Score", constraint.DefaultColumnName);
    }

    [Fact]
    public void Load_InlineDefaultWithName_CreatesNamedDefaultConstraint()
    {
        var model = DesiredSchemaLoader.Load("CREATE TABLE dbo.Users (Id int NOT NULL, Score int CONSTRAINT DF_MyDefault DEFAULT (0) NULL)");
        var table = model.Tables.Values.Single();

        Assert.True(table.Constraints.ContainsKey("DF_MYDEFAULT"));
        var constraint = table.Constraints["DF_MYDEFAULT"];
        Assert.Equal(ConstraintKind.Default, constraint.Kind);
        Assert.Equal("DF_MyDefault", constraint.Name);
        Assert.Equal("(0)", constraint.Definition);
        Assert.Equal("Score", constraint.DefaultColumnName);
    }

    [Fact]
    public void Load_InlineDefault_ColumnDefaultExpressionStillSet()
    {
        var model = DesiredSchemaLoader.Load("CREATE TABLE dbo.Users (Id int NOT NULL, Score int DEFAULT (0) NULL)");
        var table = model.Tables.Values.Single();
        var column = table.Columns["SCORE"];

        Assert.Equal("(0)", column.DefaultExpression);
    }

    [Fact]
    public void Load_AlterTableAddNotNullColumn_IsSkipped()
    {
        var model = DesiredSchemaLoader.Load(
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
