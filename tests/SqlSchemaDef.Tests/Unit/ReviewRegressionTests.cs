using System;
using System.Collections.Generic;
using System.Linq;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class ReviewRegressionTests
{
    [Fact]
    public void DefaultPlan_DoesNotDeleteOmittedColumn()
    {
        var plan = Diff("CREATE TABLE dbo.T (Id int NOT NULL, Secret nvarchar(20) NULL)", "CREATE TABLE dbo.T (Id int NOT NULL)");
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Skipped, item => item.Reason == SkippedReason.DropNotSupported && item.Target.Name == "Secret");
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.T (Id int NULL)", "CREATE TABLE dbo.T (Id bigint NULL)")]
    [InlineData("CREATE TABLE dbo.T (Id int NULL, CONSTRAINT CK_T CHECK(Id > 0))", "CREATE TABLE dbo.T (Id int NULL, CONSTRAINT CK_T CHECK(Id > 1))")]
    [InlineData("CREATE TABLE dbo.T (Id int NULL); CREATE INDEX IX_T ON dbo.T(Id)", "CREATE TABLE dbo.T (Id int NULL)")]
    public void DefaultPlan_ContainsOnlyAdditiveOperations(string currentSql, string desiredSql)
    {
        var plan = Diff(currentSql, desiredSql);
        Assert.Empty(plan.Operations);
        Assert.NotEmpty(plan.Skipped);
    }

    [Theory]
    [InlineData(OperationKind.DropColumn)]
    [InlineData(OperationKind.AlterColumn)]
    [InlineData(OperationKind.RecreateConstraint)]
    [InlineData(OperationKind.RecreateIndex)]
    [InlineData(OperationKind.RecreateForeignKey)]
    [InlineData(OperationKind.DropDescription)]
    [InlineData(OperationKind.DropForeignKey)]
    [InlineData(OperationKind.DropIndex)]
    [InlineData(OperationKind.DropConstraint)]
    [InlineData(OperationKind.DropTable)]
    public void ApplyValidation_RequiresExplicitNonAdditivePermission(OperationKind kind)
    {
        var plan = new MigrationPlan(new PlanMetadata(), new[] { new SqlOperation { Kind = kind, Sql = "-- reviewed SQL" } }, Array.Empty<SkippedItem>());
        Assert.Throws<InvalidOperationException>(() => PlanValidator.ValidateForApply(plan));
        PlanValidator.ValidateForApply(plan, true);
    }

    [Fact]
    public void ColumnLevelConstraints_ArePreserved()
    {
        const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL CONSTRAINT PK_Parent PRIMARY KEY); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL CONSTRAINT UQ_Child UNIQUE, "
            + "Age int NULL CONSTRAINT CK_Child CHECK(Age >= 0), "
            + "ParentId int NULL CONSTRAINT FK_Child REFERENCES dbo.Parent(Id))";
        var plan = Diff(string.Empty, desired);
        var sql = plan.ToScript();
        Assert.Contains("CONSTRAINT [PK_Parent] PRIMARY KEY ([Id])", sql);
        Assert.Contains("CONSTRAINT [UQ_Child] UNIQUE ([Id])", sql);
        Assert.Contains("CONSTRAINT [CK_Child] CHECK", sql);
        Assert.Contains("FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Parent] ([Id])", sql);
    }

    [Fact]
    public void ColumnLevelPrimaryKey_InfersNotNull()
    {
        var table = DesiredSchemaLoader.Load("CREATE TABLE dbo.T (Id int CONSTRAINT PK_T PRIMARY KEY)").Tables.Values.Single();
        Assert.False(table.Columns["ID"].IsNullable);
    }

    [Fact]
    public void ColumnLevelUnnamedConstraint_IsRejected()
    {
        Assert.Throws<UnsupportedDesiredFeatureException>(() => DesiredSchemaLoader.Load("CREATE TABLE dbo.T (Id int PRIMARY KEY)"));
    }

    [Fact]
    public void AlterAdd_ColumnLevelCheck_IsPreserved()
    {
        var plan = Diff("CREATE TABLE dbo.T (Id int NULL)", "CREATE TABLE dbo.T (Id int NULL); ALTER TABLE dbo.T ADD Age int NULL CONSTRAINT CK_T CHECK(Age >= 0)");
        Assert.Contains(plan.Operations, operation => operation.Kind == OperationKind.AddConstraint && operation.Sql.Contains("[CK_T]"));
    }

    [Fact]
    public void ClusteredIndexReplacement_DropsOriginalOnlyOnce()
    {
        const string table = "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); ";
        var plan = Diff(table + "CREATE CLUSTERED INDEX IX_Old ON dbo.T(Id)", table + "CREATE CLUSTERED INDEX IX_New ON dbo.T(Val)", true);
        Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateIndex, plan.Operations[0].Kind);
        Assert.Contains("DROP INDEX [IX_Old]", plan.Operations[0].Sql);
        Assert.Contains("CREATE CLUSTERED INDEX [IX_New]", plan.Operations[0].Sql);
    }

    [Fact]
    public void ClusteredIndexReplacement_PreservesDesiredNonclusteredOriginal()
    {
        const string table = "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); ";
        var plan = Diff(table + "CREATE CLUSTERED INDEX IX_Old ON dbo.T(Id)",
            table + "CREATE NONCLUSTERED INDEX IX_Old ON dbo.T(Id); CREATE CLUSTERED INDEX IX_New ON dbo.T(Val)", true);
        Assert.Equal(2, plan.Operations.Count);
        Assert.Equal(OperationKind.RecreateIndex, plan.Operations[0].Kind);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Old]", plan.Operations[0].Sql);
        Assert.Equal(OperationKind.CreateIndex, plan.Operations[1].Kind);
        Assert.Contains("CREATE CLUSTERED INDEX [IX_New]", plan.Operations[1].Sql);
    }

    [Fact]
    public void ClusteredIndexReplacement_WithoutPermission_SkipsDependentCreate()
    {
        const string table = "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL); ";
        var plan = Diff(table + "CREATE CLUSTERED INDEX IX_Old ON dbo.T(Id)",
            table + "CREATE NONCLUSTERED INDEX IX_Old ON dbo.T(Id); CREATE CLUSTERED INDEX IX_New ON dbo.T(Val)");
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Skipped, item => item.Target.Name == "IX_New");
    }

    [Fact]
    public void Rebuild_DefaultIsCreatedOnlyOnce()
    {
        var current = DesiredSchemaLoader.Load("CREATE TABLE dbo.T (Id int NOT NULL, Val varchar(20) NULL DEFAULT 'x')");
        var desired = DesiredSchemaLoader.Load("CREATE TABLE dbo.T (Id int IDENTITY NOT NULL, Val varchar(20) NULL DEFAULT 'x')");
        var plan = SchemaDiffer.Diff(current, desired, new PlanMetadata(), new PlannerOptions { EmitProposals = true });
        var proposal = Assert.Single(plan.Proposals);
        Assert.Contains("DEFAULT 'x'", proposal.Script);
        Assert.DoesNotContain("ADD CONSTRAINT [DF_T_Val] DEFAULT", proposal.Script);
    }

    [Theory]
    [InlineData("1000", "10")]
    [InlineData("-100", "-5")]
    public void IdentityParameters_ArePreservedInGeneratedSql(string seed, string increment)
    {
        var plan = Diff(string.Empty, $"CREATE TABLE dbo.T (Id bigint IDENTITY({seed},{increment}) NOT NULL)");
        Assert.Contains($"IDENTITY({seed},{increment})", Assert.Single(plan.Operations).Sql);
    }

    [Fact]
    public void IdentityParameters_SurviveCatalogAndExportRoundTrip()
    {
        var current = CurrentSchemaReader.BuildModel(
            new[] { new CurrentSchemaReader.TableRow { ObjectId = 1, SchemaName = "dbo", TableName = "T" } },
            new[] { new CurrentSchemaReader.ColumnRow { ObjectId = 1, ColumnId = 1, ColumnName = "Id", TypeName = "bigint", IsIdentity = true, IdentitySeed = "1000", IdentityIncrement = "10" } });
        var script = SchemaExportScriptBuilder.BuildScript(current, new ExportOptions(), new List<SkippedItem>());
        Assert.Contains("IDENTITY(1000,10)", script);
        var plan = SchemaDiffer.Diff(current, DesiredSchemaLoader.Load(script), new PlanMetadata());
        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Theory]
    [InlineData("1", "1", false)]
    [InlineData("1000", "1", true)]
    [InlineData("1", "10", true)]
    public void IdentityParameterChanges_AreDetected(string seed, string increment, bool expectChange)
    {
        var plan = Diff("CREATE TABLE dbo.T (Id int IDENTITY NOT NULL)", $"CREATE TABLE dbo.T (Id int IDENTITY({seed},{increment}) NOT NULL)");
        Assert.Empty(plan.Operations);
        Assert.Equal(expectChange, plan.Skipped.Any(item => item.Reason == SkippedReason.AlterNotSupported));
    }

    [Fact]
    public void PrimaryKeyRecreation_UsesDesiredForeignKey()
    {
        const string current = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id))";
        const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id) ON DELETE CASCADE)";
        var plan = Diff(current, desired, true);
        Assert.Contains("ON DELETE CASCADE", Assert.Single(plan.Operations.Where(operation => operation.Kind == OperationKind.RecreateForeignKey)).Sql);
    }

    [Fact]
    public void PrimaryKeyRecreation_DoesNotRestoreRemovedForeignKey()
    {
        const string current = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id))";
        const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED(Id)); CREATE TABLE dbo.Child (Id int NOT NULL)";
        var plan = Diff(current, desired, true);
        Assert.DoesNotContain("ADD CONSTRAINT [FK_Child]", plan.ToScript());
    }

    [Fact]
    public void PrimaryKeyRecreation_PreservesForeignKeyOnExcludedTable()
    {
        const string currentSql = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id))";
        var current = DesiredSchemaLoader.Load(currentSql);
        var desired = DesiredSchemaLoader.Load("CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED(Id))");
        var plan = SchemaDiffer.Diff(current, desired, new PlanMetadata(), new PlannerOptions { AllowDrop = true, IncludeTablePatterns = new[] { "Parent" } });
        Assert.Contains("ADD CONSTRAINT [FK_Child]", Assert.Single(plan.Operations.Where(operation => operation.Kind == OperationKind.RecreateForeignKey)).Sql);
    }

    [Fact]
    public void PrimaryKeyRecreation_RestoresForeignKeyAfterNewReferencedKey()
    {
        const string current = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id))";
        const string desired = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY NONCLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.ZNewParent(Id)); "
            + "CREATE TABLE dbo.ZNewParent (Id int NOT NULL, CONSTRAINT PK_NewParent PRIMARY KEY(Id))";
        var plan = Diff(current, desired, true);
        var sql = plan.ToScript();
        var referencedKey = sql.IndexOf("ADD CONSTRAINT [PK_NewParent]", StringComparison.Ordinal);
        var restoredForeignKey = sql.IndexOf("ADD CONSTRAINT [FK_Child]", StringComparison.Ordinal);
        Assert.True(referencedKey >= 0 && restoredForeignKey > referencedKey);
    }

    [Fact]
    public void PrimaryKeyRecreation_WithoutPermission_DoesNotRestoreExistingForeignKey()
    {
        const string current = "CREATE TABLE dbo.Parent (Id int NOT NULL, CONSTRAINT PK_Parent PRIMARY KEY CLUSTERED(Id)); "
            + "CREATE TABLE dbo.Child (Id int NOT NULL, CONSTRAINT FK_Child FOREIGN KEY(Id) REFERENCES dbo.Parent(Id))";
        var plan = Diff(current, current.Replace("PRIMARY KEY CLUSTERED", "PRIMARY KEY NONCLUSTERED"));
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Skipped, item => item.Target.Name == "FK_Child");
    }

    [Fact]
    public void CheckIdentifierCaseChange_IsIgnored()
    {
        var plan = Diff("CREATE TABLE dbo.T (Val int NULL, CONSTRAINT CK_T CHECK(Val > 0))", "CREATE TABLE dbo.T (Val int NULL, CONSTRAINT CK_T CHECK(val > 0))");
        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Theory]
    [InlineData("'A'", "'a'")]
    [InlineData("N'A''B'", "N'a''B'")]
    [InlineData("'[A-Z]'", "'[a-z]'")]
    public void CheckLiteralCaseChange_IsDetected(string currentLiteral, string desiredLiteral)
    {
        const string table = "CREATE TABLE dbo.T (Val varchar(10) COLLATE Latin1_General_100_CS_AS NOT NULL, CONSTRAINT CK_T CHECK(Val = ";
        var plan = Diff(table + currentLiteral + "))", table + desiredLiteral + "))", true);
        Assert.Equal(OperationKind.RecreateConstraint, Assert.Single(plan.Operations).Kind);
        Assert.Contains(desiredLiteral, plan.Operations[0].Sql);
    }

    private static MigrationPlan Diff(string currentSql, string desiredSql, bool allowDrop = false)
    {
        return SchemaDiffer.Diff(DesiredSchemaLoader.Load(currentSql), DesiredSchemaLoader.Load(desiredSql), new PlanMetadata(), new PlannerOptions { AllowDrop = allowDrop });
    }
}
