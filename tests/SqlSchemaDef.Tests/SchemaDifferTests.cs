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
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateTable, op.Kind);
        Assert.Equal("CREATE TABLE dbo.Users (Id INT NOT NULL)", op.Sql);
        Assert.Equal("dbo.Users", op.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenIdentifiersAreKeywords_EscapesInSql()
    {
        const string desiredSql = "CREATE TABLE dbo.[User] ([Select] int NOT NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal("CREATE TABLE dbo.[User] ([Select] INT NOT NULL)", op.Sql);
    }

    [Fact]
    public void Diff_WhenColumnMissing_EmitsAddColumn()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(100) NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddColumn, op.Kind);
        Assert.Equal("ALTER TABLE dbo.Users ADD Name NVARCHAR (100) NULL", op.Sql);
        Assert.Equal("dbo.Users.Name", op.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenNotNullColumnMissing_IsSkipped()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NOT NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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

        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };
        currentTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = true };
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
    public void Diff_WhenIndexHasIncludeAndSortOrder_EmitsCreateIndex()
    {
        var desiredSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (",
            "  Id int NOT NULL,",
            "  Name nvarchar(100) NULL,",
            "  Age int NULL",
            ")",
            "CREATE INDEX IX_Users_Name ON dbo.Users (Name DESC) INCLUDE (Age)",
        });

        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = true };
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateIndex, op.Kind);
        Assert.Equal("CREATE INDEX IX_Users_Name ON dbo.Users (Name DESC) INCLUDE (Age)", op.Sql);
    }

    [Fact]
    public void Diff_WhenCreatingTableWithIdentityAndDefault_EmitsDefinition()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int IDENTITY(1,1) NOT NULL, Score int DEFAULT (0) NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateTable, op.Kind);
        Assert.Contains("IDENTITY", op.Sql);
        Assert.Contains("DEFAULT (0)", op.Sql);
    }

    [Fact]
    public void Diff_WhenAddingColumnWithDefault_EmitsDefaultExpression()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Score int DEFAULT (1) NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddColumn, op.Kind);
        Assert.Contains("DEFAULT (1)", op.Sql);
    }

    [Fact]
    public void Diff_WhenConstraintNameNeedsEscaping_EscapesConstraintName()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, CONSTRAINT [Order] PRIMARY KEY (Id))";
        var desired = DesiredSchemaLoader.Load(desiredSql);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddConstraint, op.Kind);
        Assert.Contains("ADD CONSTRAINT [Order] PRIMARY KEY", op.Sql);
    }

    [Fact]
    public void Diff_WhenIndexNameNeedsEscaping_EscapesIndexName()
    {
        var desiredSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (Id int NOT NULL)",
            "CREATE INDEX [Order] ON dbo.Users (Id)",
        });
        var desired = DesiredSchemaLoader.Load(desiredSql);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateIndex, op.Kind);
        Assert.Contains("CREATE INDEX [Order] ON dbo.Users (Id)", op.Sql);
    }

    [Fact]
    public void Diff_WhenCheckConstraintDefinitionHasNoParentheses_WrapsIt()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredUsers.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };
        desiredUsers.Constraints["CK_USERS_AGE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Check,
            Name = "CK_Users_Age",
            Definition = "Age > 0",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentUsers.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var operation = Assert.Single(plan.Operations);
        Assert.Contains("CONSTRAINT CK_Users_Age CHECK (Age > 0)", operation.Sql);
    }

    [Fact]
    public void Diff_WhenTableOnlyInCurrent_IsSkipped()
    {
        var desired = new DatabaseModel();
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "OldTable");

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
        var firstPlan = SchemaDiffer.Diff(new DatabaseModel(), firstDesired, metadata);
        var secondPlan = SchemaDiffer.Diff(new DatabaseModel(), secondDesired, metadata);

        var firstTargets = firstPlan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        var secondTargets = secondPlan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();

        Assert.Equal(firstTargets, secondTargets);
        Assert.Equal(new[] { "dbo.Alpha", "dbo.Beta" }, firstTargets);
    }

    [Fact]
    public void Diff_IsDeterministicAcrossBatches()
    {
        var firstSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Beta (Id int NOT NULL)",
            "GO",
            "CREATE TABLE dbo.Alpha (Id int NOT NULL)",
            "GO",
            "ALTER TABLE dbo.Beta ADD CONSTRAINT PK_Beta PRIMARY KEY (Id)",
            "GO",
            "ALTER TABLE dbo.Alpha ADD CONSTRAINT PK_Alpha PRIMARY KEY (Id)",
        });

        var secondSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Alpha (Id int NOT NULL)",
            "GO",
            "ALTER TABLE dbo.Alpha ADD CONSTRAINT PK_Alpha PRIMARY KEY (Id)",
            "GO",
            "CREATE TABLE dbo.Beta (Id int NOT NULL)",
            "GO",
            "ALTER TABLE dbo.Beta ADD CONSTRAINT PK_Beta PRIMARY KEY (Id)",
        });

        var firstDesired = DesiredSchemaLoader.Load(firstSql);
        var secondDesired = DesiredSchemaLoader.Load(secondSql);

        var metadata = new PlanMetadata { Schema = "dbo" };
        var firstPlan = SchemaDiffer.Diff(new DatabaseModel(), firstDesired, metadata);
        var secondPlan = SchemaDiffer.Diff(new DatabaseModel(), secondDesired, metadata);

        var firstTargets = firstPlan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        var secondTargets = secondPlan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();

        Assert.Equal(firstTargets, secondTargets);
        Assert.Equal(new[]
        {
            "dbo.Alpha",
            "dbo.Beta",
            "dbo.Alpha.PK_Alpha",
            "dbo.Beta.PK_Beta",
        }, firstTargets);
    }

    [Fact]
    public void Diff_SkippedOrdering_IsDeterministic()
    {
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "Beta");
        current.GetOrAddTable("dbo", "Alpha");

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, new DatabaseModel(), metadata);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = true,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);

        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Equal("dbo.Users.IX_Users_Name", skipped.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WhenIndexIncludeDiffers_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = false,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = false,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
            IncludeColumns = new[] { "Age" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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

        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
            KeyColumns = new[] { new IndexKeyColumn { Name = "Id" } },
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
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

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
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var targets = plan.Skipped.Select(item => item.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.Id",
            "dbo.Alpha.Legacy",
            "dbo.Beta.Id",
            "dbo.Beta.Old",
        }, targets);
    }

    [Fact]
    public void Diff_CurrentOnlyForeignKeysAcrossTables_AreSkippedDeterministically()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Alpha");
        desired.GetOrAddTable("dbo", "Beta");

        var current = new DatabaseModel();
        var currentAlpha = current.GetOrAddTable("dbo", "Alpha");
        currentAlpha.Constraints["FK_ALPHA_BETA"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Alpha_Beta",
            Columns = new[] { "BetaId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Beta",
            ReferenceColumns = new[] { "Id" },
        };

        var currentBeta = current.GetOrAddTable("dbo", "Beta");
        currentBeta.Constraints["FK_BETA_ALPHA"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Beta_Alpha",
            Columns = new[] { "AlphaId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Alpha",
            ReferenceColumns = new[] { "Id" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var targets = plan.Skipped.Select(item => item.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.FK_Alpha_Beta",
            "dbo.Beta.FK_Beta_Alpha",
        }, targets);
    }

    [Fact]
    public void Diff_WithEmitProposalsFalse_ProposalsIsEmpty()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age bigint NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Proposals);
        Assert.NotEmpty(plan.Skipped);
    }

    [Fact]
    public void Diff_WithEmitProposalsTrue_ColumnTypeDiff_ProducesProposal()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age bigint NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Single(plan.Proposals);
        Assert.Equal("Users", plan.Proposals[0].Target.Name);
        Assert.Contains("Age", plan.Proposals[0].Description);
    }

    [Fact]
    public void Diff_WithEmitProposalsTrue_SkippedItemsStillPresent()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age bigint NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.NotEmpty(plan.Skipped);
        Assert.NotEmpty(plan.Proposals);
    }
}
