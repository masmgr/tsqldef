using System.Linq;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class SchemaDifferTests
{
    [Fact]
    public void Diff_WhenTableMissing_EmitsCreateTable()
    {
        var desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";
        var desired = new DesiredSchemaLoader().Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateTable, op.Kind);
        Assert.Equal("CREATE TABLE dbo.Users (Id INT NOT NULL)", op.Sql);
        Assert.Equal("dbo.Users", op.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenColumnMissing_EmitsAddColumn()
    {
        var desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(100) NULL)";
        var desired = new DesiredSchemaLoader().Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel
        {
            Name = "Id",
            SqlType = "int",
            IsNullable = false,
            IsIdentity = false,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddColumn, op.Kind);
        Assert.Equal("ALTER TABLE dbo.Users ADD Name NVARCHAR (100) NULL", op.Sql);
        Assert.Equal("dbo.Users.Name", op.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenNotNullColumnMissing_IsSkipped()
    {
        var desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NOT NULL)";
        var desired = new DesiredSchemaLoader().Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel
        {
            Name = "Id",
            SqlType = "int",
            IsNullable = false,
            IsIdentity = false,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);

        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.NotNullAddNotSupported, skipped.Reason);
        Assert.Equal("dbo.Users.Age", skipped.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenConstraintsAndIndexesMissing_EmitsOperationsInOrder()
    {
        var desiredSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  TeamId int NOT NULL,",
            "  Name nvarchar(100) NULL,",
            "  Age int NULL,",
            "  CONSTRAINT PK_Users PRIMARY KEY (Id),",
            "  CONSTRAINT UQ_Users_Name UNIQUE (Name),",
            "  CONSTRAINT CK_Users_Age CHECK (Age > 0)",
            ")",
            "CREATE INDEX IX_Users_Name ON dbo.Users (Name)",
            "ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)",
        });

        var desired = new DesiredSchemaLoader().Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };
        currentTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = true };
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var kinds = plan.Operations.Select(op => op.Kind).ToArray();
        Assert.Equal(new[]
        {
            OperationKind.AddConstraint,
            OperationKind.AddConstraint,
            OperationKind.AddConstraint,
            OperationKind.CreateIndex,
            OperationKind.AddForeignKey,
        }, kinds);

        var constraintSql = plan.Operations.Take(3).Select(op => op.Sql).ToArray();
        Assert.Contains(constraintSql, sql => sql.Contains("CONSTRAINT PK_Users PRIMARY KEY"));
        Assert.Contains(constraintSql, sql => sql.Contains("CONSTRAINT UQ_Users_Name UNIQUE"));
        Assert.Contains(constraintSql, sql => sql.Contains("CONSTRAINT CK_Users_Age CHECK"));
        Assert.Contains("CREATE INDEX IX_Users_Name", plan.Operations[3].Sql);
        Assert.Contains("FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)", plan.Operations[4].Sql);
    }

    [Fact]
    public void Diff_WhenTableOnlyInCurrent_IsSkipped()
    {
        var desired = new DatabaseModel();
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "OldTable");

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.DropNotSupported, skipped.Reason);
        Assert.Equal("dbo.OldTable", skipped.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenColumnDefinitionDiffers_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["AGE"] = new ColumnModel
        {
            Name = "Age",
            SqlType = "int",
            IsNullable = true,
            IsIdentity = false,
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["AGE"] = new ColumnModel
        {
            Name = "Age",
            SqlType = "bigint",
            IsNullable = true,
            IsIdentity = false,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);

        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Equal("dbo.Users.Age", skipped.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_IsDeterministicAcrossInsertionOrder()
    {
        var firstDesired = new DatabaseModel();
        var firstAlpha = firstDesired.GetOrAddTable("dbo", "Alpha");
        firstAlpha.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var firstBeta = firstDesired.GetOrAddTable("dbo", "Beta");
        firstBeta.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var secondDesired = new DatabaseModel();
        var secondBeta = secondDesired.GetOrAddTable("dbo", "Beta");
        secondBeta.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var secondAlpha = secondDesired.GetOrAddTable("dbo", "Alpha");
        secondAlpha.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var firstPlan = new SchemaDiffer().Diff(new DatabaseModel(), firstDesired, metadata);
        var secondPlan = new SchemaDiffer().Diff(new DatabaseModel(), secondDesired, metadata);

        var firstTargets = firstPlan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        var secondTargets = secondPlan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();

        Assert.Equal(firstTargets, secondTargets);
        Assert.Equal(new[] { "dbo.Alpha", "dbo.Beta" }, firstTargets);
    }

    [Fact]
    public void Diff_SkippedOrdering_IsDeterministic()
    {
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "Beta");
        current.GetOrAddTable("dbo", "Alpha");

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, new DatabaseModel(), metadata);

        var targets = plan.Skipped.Select(item => item.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[] { "dbo.Alpha", "dbo.Beta" }, targets);
    }

    [Fact]
    public void Diff_WhenConstraintDefinitionDiffers_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id", "Name" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);

        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Equal("dbo.Users.PK_Users", skipped.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenIndexDefinitionDiffers_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = false,
            KeyColumns = new[] { "Name" },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = true,
            KeyColumns = new[] { "Name" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);

        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Equal("dbo.Users.IX_Users_Name", skipped.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_CurrentOnlyConstraintsAndIndexes_AreSkippedInOrder()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Users");

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Constraints["UQ_USERS_NAME"] = new ConstraintModel
        {
            Kind = ConstraintKind.Unique,
            Name = "UQ_Users_Name",
            Columns = new[] { "Name" },
        };
        currentTable.Constraints["CK_USERS_AGE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Check,
            Name = "CK_Users_Age",
            Definition = "(Age > 0)",
        };
        currentTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = false,
            KeyColumns = new[] { "Name" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var targets = plan.Skipped.Select(item => item.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Users.CK_Users_Age",
            "dbo.Users.UQ_Users_Name",
            "dbo.Users.IX_Users_Name",
        }, targets);
    }

    [Fact]
    public void Diff_ForeignKey_IsOrderedAfterTableCreates()
    {
        var desiredSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Teams (Id int NOT NULL)",
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  TeamId int NOT NULL,",
            "  CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)",
            ")",
        });

        var desired = new DesiredSchemaLoader().Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        Assert.Equal(3, plan.Operations.Count);
        Assert.Equal(OperationKind.CreateTable, plan.Operations[0].Kind);
        Assert.Equal(OperationKind.CreateTable, plan.Operations[1].Kind);
        Assert.Equal(OperationKind.AddForeignKey, plan.Operations[2].Kind);
    }

    [Fact]
    public void Diff_CurrentOnlyItemsAcrossTables_AreSkippedDeterministically()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Alpha");
        desired.GetOrAddTable("dbo", "Beta");

        var current = new DatabaseModel();
        var currentAlpha = current.GetOrAddTable("dbo", "Alpha");
        currentAlpha.Constraints["CK_ALPHA"] = new ConstraintModel
        {
            Kind = ConstraintKind.Check,
            Name = "CK_Alpha",
            Definition = "(1=1)",
        };
        currentAlpha.Indexes["IX_ALPHA"] = new IndexModel
        {
            Name = "IX_Alpha",
            IsUnique = false,
            KeyColumns = new[] { "Id" },
        };

        var currentBeta = current.GetOrAddTable("dbo", "Beta");
        currentBeta.Constraints["UQ_BETA"] = new ConstraintModel
        {
            Kind = ConstraintKind.Unique,
            Name = "UQ_Beta",
            Columns = new[] { "Name" },
        };
        currentBeta.Indexes["IX_BETA"] = new IndexModel
        {
            Name = "IX_Beta",
            IsUnique = false,
            KeyColumns = new[] { "Name" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var targets = plan.Skipped.Select(item => item.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.CK_Alpha",
            "dbo.Alpha.IX_Alpha",
            "dbo.Beta.UQ_Beta",
            "dbo.Beta.IX_Beta",
        }, targets);
    }

    [Fact]
    public void Diff_CurrentOnlyColumnsAcrossTables_AreSkippedDeterministically()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Alpha");
        desired.GetOrAddTable("dbo", "Beta");

        var current = new DatabaseModel();
        var currentAlpha = current.GetOrAddTable("dbo", "Alpha");
        currentAlpha.Columns["ID"] = new ColumnModel
        {
            Name = "Id",
            SqlType = "int",
            IsNullable = false,
        };
        currentAlpha.Columns["LEGACY"] = new ColumnModel
        {
            Name = "Legacy",
            SqlType = "int",
            IsNullable = true,
        };

        var currentBeta = current.GetOrAddTable("dbo", "Beta");
        currentBeta.Columns["ID"] = new ColumnModel
        {
            Name = "Id",
            SqlType = "int",
            IsNullable = false,
        };
        currentBeta.Columns["OLD"] = new ColumnModel
        {
            Name = "Old",
            SqlType = "int",
            IsNullable = true,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = new SchemaDiffer().Diff(current, desired, metadata);

        var targets = plan.Skipped.Select(item => item.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.Id",
            "dbo.Alpha.Legacy",
            "dbo.Beta.Id",
            "dbo.Beta.Old",
        }, targets);
    }
}
