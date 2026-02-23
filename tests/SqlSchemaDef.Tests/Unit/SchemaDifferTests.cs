using System.Collections.Generic;
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
        Assert.Equal("CREATE TABLE [dbo].[Users] ([Id] INT NOT NULL)", op.Sql);
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
        Assert.Equal("CREATE TABLE [dbo].[User] ([Select] INT NOT NULL)", op.Sql);
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
        Assert.Equal("ALTER TABLE [dbo].[Users] ADD [Name] NVARCHAR (100) NULL", op.Sql);
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
    public void Diff_WhenNotNullColumnWithDefault_IsAllowed()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Active bit DEFAULT (1) NOT NULL)";
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

        Assert.Contains(plan.Operations, op => op.Kind == OperationKind.AddColumn && op.Target.Name == "Active");
        Assert.DoesNotContain(plan.Skipped, s => s.Reason == SkippedReason.NotNullAddNotSupported);
    }

    [Fact]
    public void Diff_WhenNotNullColumnWithoutDefault_IsStillSkipped()
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
        Assert.Contains(plan.Skipped, s => s.Reason == SkippedReason.NotNullAddNotSupported);
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
        Assert.Contains(constraintSql, sql => sql.Contains("CONSTRAINT [PK_Users] PRIMARY KEY"));
        Assert.Contains(constraintSql, sql => sql.Contains("CONSTRAINT [UQ_Users_Name] UNIQUE"));
        Assert.Contains(constraintSql, sql => sql.Contains("CONSTRAINT [CK_Users_Age] CHECK"));
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Users_Name]", plan.Operations[3].Sql);
        Assert.Contains("FOREIGN KEY ([TeamId]) REFERENCES [dbo].[Teams] ([Id])", plan.Operations[4].Sql);
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
        Assert.Equal("CREATE NONCLUSTERED INDEX [IX_Users_Name] ON [dbo].[Users] ([Name] DESC) INCLUDE ([Age])", op.Sql);
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

        // DEFAULT is emitted inline in the AddColumn SQL; no separate AddConstraint for new columns.
        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddColumn, op.Kind);
        Assert.Contains("DEFAULT (1)", op.Sql);
    }

    [Fact]
    public void Diff_WhenAddingDefaultToExistingColumn_EmitsAddConstraint()
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
        currentTable.Columns["SCORE"] = new ColumnModel
        {
            Name = "Score",
            SqlType = "int",
            IsNullable = true,
            IsIdentity = false,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddConstraint, op.Kind);
        Assert.Contains("CONSTRAINT [DF_Users_Score] DEFAULT (1) FOR [Score]", op.Sql);
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
        Assert.Contains("CREATE NONCLUSTERED INDEX [Order] ON [dbo].[Users] ([Id])", op.Sql);
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
        Assert.Contains("CONSTRAINT [CK_Users_Age] CHECK (Age > 0)", operation.Sql);
    }

    [Fact]
    public void Diff_WhenCheckConstraintPatternLiteralHasBrackets_PreservesLiteralDifference()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["CODE"] = new ColumnModel { Name = "Code", SqlType = "nvarchar(10)", IsNullable = false };
        desiredUsers.Constraints["CK_USERS_CODE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Check,
            Name = "CK_Users_Code",
            Definition = "[Code] LIKE 'A-Z'",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["CODE"] = new ColumnModel { Name = "Code", SqlType = "nvarchar(10)", IsNullable = false };
        currentUsers.Constraints["CK_USERS_CODE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Check,
            Name = "CK_Users_Code",
            Definition = "[Code] LIKE '[A-Z]'",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateConstraint, op.Kind);
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
    public void Diff_WhenIndexDefinitionDiffers_EmitsRecreateIndex()
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

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateIndex, op.Kind);
        Assert.Contains("DROP INDEX", op.Sql);
        Assert.Contains("CREATE", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenIndexIncludeDiffers_EmitsRecreateIndex()
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

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateIndex, op.Kind);
        Assert.Contains("DROP INDEX", op.Sql);
        Assert.Contains("CREATE", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_CurrentOnlyConstraintsAndIndexes_EmitsDropOperationsInOrder()
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

        Assert.Empty(plan.Skipped);

        // Order: DropIndex → DropConstraint (alphabetical within each)
        var kinds = plan.Operations.Select(op => op.Kind).ToArray();
        Assert.Equal(new[]
        {
            OperationKind.DropIndex,
            OperationKind.DropConstraint,
            OperationKind.DropConstraint,
        }, kinds);

        var targets = plan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Users.IX_Users_Name",
            "dbo.Users.CK_Users_Age",
            "dbo.Users.UQ_Users_Name",
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
    public void Diff_CurrentOnlyItemsAcrossTables_EmitsDropOperationsDeterministically()
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

        Assert.Empty(plan.Skipped);

        // Order: DropIndex (Alpha, Beta) → DropConstraint (Alpha, Beta)
        var targets = plan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.IX_Alpha",
            "dbo.Beta.IX_Beta",
            "dbo.Alpha.CK_Alpha",
            "dbo.Beta.UQ_Beta",
        }, targets);
    }

    [Fact]
    public void Diff_CurrentOnlyColumnsAcrossTables_EmitsDropColumnsDeterministically()
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

        Assert.Empty(plan.Skipped);
        Assert.All(plan.Operations, op => Assert.Equal(OperationKind.DropColumn, op.Kind));

        var targets = plan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.Id",
            "dbo.Alpha.Legacy",
            "dbo.Beta.Id",
            "dbo.Beta.Old",
        }, targets);
    }

    [Fact]
    public void Diff_CurrentOnlyForeignKeysAcrossTables_EmitsDropForeignKeysDeterministically()
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

        Assert.Empty(plan.Skipped);
        Assert.All(plan.Operations, op => Assert.Equal(OperationKind.DropForeignKey, op.Kind));

        var targets = plan.Operations.Select(op => op.Target.ToDisplayName()).ToArray();
        Assert.Equal(new[]
        {
            "dbo.Alpha.FK_Alpha_Beta",
            "dbo.Beta.FK_Beta_Alpha",
        }, targets);
    }

    [Fact]
    public void Diff_ForeignKeyWithDeleteCascade_EmitsOnDeleteClause()
    {
        var desired = new DatabaseModel();
        var desiredTeams = desired.GetOrAddTable("dbo", "Teams");
        desiredTeams.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredUsers.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };
        desiredUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
            DeleteAction = "CASCADE",
        };

        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "Teams").Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentUsers.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddForeignKey, op.Kind);
        Assert.Contains("ON DELETE CASCADE", op.Sql);
    }

    [Fact]
    public void Diff_ForeignKeyNoAction_OmitsOnClause()
    {
        var desired = new DatabaseModel();
        var desiredTeams = desired.GetOrAddTable("dbo", "Teams");
        desiredTeams.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredUsers.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };
        desiredUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
        };

        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "Teams").Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentUsers.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.DoesNotContain("ON DELETE", op.Sql);
        Assert.DoesNotContain("ON UPDATE", op.Sql);
    }

    [Fact]
    public void Diff_ForeignKeyActionDiffers_EmitsRecreateForeignKey()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
            DeleteAction = "CASCADE",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateForeignKey, op.Kind);
        Assert.Contains("DROP CONSTRAINT", op.Sql);
        Assert.Contains("FOREIGN KEY", op.Sql);
        Assert.Contains("CASCADE", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_ForeignKeySameAction_NoOperation()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
            DeleteAction = "CASCADE",
            UpdateAction = "SET NULL",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
            DeleteAction = "CASCADE",
            UpdateAction = "SET NULL",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_DefaultConstraintMissing_EmitsAddConstraint()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredUsers.Columns["SCORE"] = new ColumnModel { Name = "Score", SqlType = "int", IsNullable = true, DefaultExpression = "(0)" };
        desiredUsers.Constraints["DF_USERS_SCORE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Score",
            Definition = "(0)",
            DefaultColumnName = "Score",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentUsers.Columns["SCORE"] = new ColumnModel { Name = "Score", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddConstraint, op.Kind);
        Assert.Contains("CONSTRAINT [DF_Users_Score] DEFAULT (0) FOR [Score]", op.Sql);
    }

    [Fact]
    public void Diff_NewTableWithDefault_NoSeparateAddConstraint()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Score int DEFAULT (0) NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateTable, op.Kind);
        Assert.Contains("DEFAULT (0)", op.Sql);
    }

    [Fact]
    public void Diff_DefaultConstraintSame_NoOperation()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["SCORE"] = new ColumnModel { Name = "Score", SqlType = "int", IsNullable = true, DefaultExpression = "(0)" };
        desiredUsers.Constraints["DF_USERS_SCORE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Score",
            Definition = "(0)",
            DefaultColumnName = "Score",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["SCORE"] = new ColumnModel { Name = "Score", SqlType = "int", IsNullable = true, DefaultExpression = "(0)" };
        currentUsers.Constraints["DF_USERS_SCORE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Score",
            Definition = "(0)",
            DefaultColumnName = "Score",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_DefaultConstraintDiffers_EmitsRecreateConstraint()
    {
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Constraints["DF_USERS_SCORE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Score",
            Definition = "(1)",
            DefaultColumnName = "Score",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Constraints["DF_USERS_SCORE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Score",
            Definition = "(0)",
            DefaultColumnName = "Score",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateConstraint, op.Kind);
        Assert.Contains("DROP CONSTRAINT", op.Sql);
        Assert.Contains("DEFAULT", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_DefaultConstraintSame_WhenDbHasExtraParens_NoOperation()
    {
        // SQL Server stores DEFAULT (1) as ((1)) in sys.default_constraints.definition.
        // The normalizer should strip redundant outer parentheses.
        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["ACTIVE"] = new ColumnModel { Name = "Active", SqlType = "bit", IsNullable = false, DefaultExpression = "(1)" };
        desiredUsers.Constraints["DF_USERS_ACTIVE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Active",
            Definition = "(1)",
            DefaultColumnName = "Active",
        };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ACTIVE"] = new ColumnModel { Name = "Active", SqlType = "bit", IsNullable = false, DefaultExpression = "(1)" };
        currentUsers.Constraints["DF_USERS_ACTIVE"] = new ConstraintModel
        {
            Kind = ConstraintKind.Default,
            Name = "DF_Users_Active",
            Definition = "((1))",
            DefaultColumnName = "Active",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WithEmitProposalsFalse_ProposalsIsEmpty()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Proposals);
        Assert.NotEmpty(plan.Skipped);
    }

    [Fact]
    public void Diff_WithEmitProposalsTrue_ColumnTypeDiff_ProducesProposal()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };

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
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.NotEmpty(plan.Skipped);
        Assert.NotEmpty(plan.Proposals);
    }

    [Fact]
    public void Diff_WithEmitProposalsTrue_OperationsOnProposalTargetAreFilteredOut()
    {
        const string desiredSql = @"
CREATE TABLE dbo.Users (
    Id int NOT NULL,
    Age int NULL,
    CONSTRAINT CK_Users_Age CHECK (Age >= 0)
)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        var table = current.GetOrAddTable("dbo", "Users");
        table.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Single(plan.Proposals);
        Assert.Contains(plan.Skipped, s => s.Reason == SkippedReason.AlterNotSupported && s.Target != null && s.Target.Name == "Age");
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Diff_WithEmitProposalsTrue_PrimaryKeyDiff_ProducesProposal()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Name")] = new ColumnModel { Name = "Name", SqlType = "NVARCHAR(100)", IsNullable = false };
        desiredTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Name")] = new ColumnModel { Name = "Name", SqlType = "NVARCHAR(100)", IsNullable = false };
        currentTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id", "Name" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal("Users", proposal.Target.Name);
        Assert.Contains("primary key change", proposal.Description);
    }

    [Fact]
    public void Diff_WithEmitProposalsTrue_RecreateOpsOnProposalTargetAreFilteredOut()
    {
        // Table has both column rebuild and index recreate — index recreate should be filtered
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };
        desiredTable.Indexes[IdentifierHelper.NormalizeNameKey("IX_Users_Age")] = new IndexModel
        {
            Name = "IX_Users_Age",
            IsUnique = true,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Age" } },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };
        currentTable.Indexes[IdentifierHelper.NormalizeNameKey("IX_Users_Age")] = new IndexModel
        {
            Name = "IX_Users_Age",
            IsUnique = false,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Age" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Single(plan.Proposals);

        // RecreateIndex operation should be filtered out because rebuild covers the same table
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Diff_WhenTableDescriptionMissing_EmitsAddDescription()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Description = "User accounts";

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddDescription, op.Kind);
        Assert.Contains("sp_addextendedproperty", op.Sql);
        Assert.Contains("User accounts", op.Sql);
    }

    [Fact]
    public void Diff_WhenColumnDescriptionMissing_EmitsAddDescription()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "Primary key" };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddDescription, op.Kind);
        Assert.Contains("sp_addextendedproperty", op.Sql);
        Assert.Contains("Primary key", op.Sql);
        Assert.Contains("COLUMN", op.Sql);
    }

    [Fact]
    public void Diff_WhenTableDescriptionDiffers_EmitsUpdateDescription()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Description = "Updated description";

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Description = "Old description";

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.UpdateDescription, op.Kind);
        Assert.Contains("sp_updateextendedproperty", op.Sql);
        Assert.Contains("Updated description", op.Sql);
    }

    [Fact]
    public void Diff_WhenColumnDescriptionDiffers_EmitsUpdateDescription()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "New desc" };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "Old desc" };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.UpdateDescription, op.Kind);
        Assert.Contains("sp_updateextendedproperty", op.Sql);
    }

    [Fact]
    public void Diff_WhenDescriptionsSame_NoOperation()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "Same desc" };
        desiredTable.Description = "Table desc";

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "Same desc" };
        currentTable.Description = "Table desc";

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenCurrentOnlyDescription_EmitsDropDescription()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "Old desc" };
        currentTable.Description = "Old table desc";

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Skipped);
        Assert.Equal(2, plan.Operations.Count);
        Assert.All(plan.Operations, op => Assert.Equal(OperationKind.DropDescription, op.Kind));
    }

    [Fact]
    public void Diff_WhenNewTableHasDescription_EmitsCreateTableAndAddDescription()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Description = "User accounts";

        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Equal(2, plan.Operations.Count);
        Assert.Equal(OperationKind.CreateTable, plan.Operations[0].Kind);
        Assert.Equal(OperationKind.AddDescription, plan.Operations[1].Kind);
    }

    [Fact]
    public void Diff_DescriptionsOrderedAfterForeignKeys()
    {
        var desired = new DatabaseModel();
        var desiredTeams = desired.GetOrAddTable("dbo", "Teams");
        desiredTeams.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredUsers.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };
        desiredUsers.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
        };
        desiredUsers.Description = "User accounts";

        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "Teams").Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentUsers.Columns["TEAMID"] = new ColumnModel { Name = "TeamId", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var kinds = plan.Operations.Select(op => op.Kind).ToArray();
        Assert.Equal(new[] { OperationKind.AddForeignKey, OperationKind.AddDescription }, kinds);
    }

    [Fact]
    public void Diff_WhenNewTableHasColumnWithCollation_EmitsCollationInCreateTable()
    {
        const string desiredSql = "CREATE TABLE dbo.T (Name nvarchar(100) COLLATE Japanese_CI_AS NOT NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateTable, op.Kind);
        Assert.Contains("COLLATE Japanese_CI_AS", op.Sql);
    }

    [Fact]
    public void Diff_WhenAddingColumnWithCollation_EmitsCollationInAddColumn()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nALTER TABLE dbo.T ADD Name nvarchar(100) COLLATE Latin1_General_CI_AS NULL";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddColumn, op.Kind);
        Assert.Contains("COLLATE Latin1_General_CI_AS", op.Sql);
    }

    [Fact]
    public void Diff_WhenExistingColumnHasDifferentCollation_EmitsAlterColumn()
    {
        const string desiredSql = "CREATE TABLE dbo.T (Name nvarchar(100) COLLATE Japanese_CI_AS NOT NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["NAME"] = new ColumnModel
        {
            Name = "Name",
            SqlType = "nvarchar(100)",
            IsNullable = false,
            Collation = "SQL_Latin1_General_CP1_CI_AS",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AlterColumn, op.Kind);
        Assert.Contains("ALTER COLUMN", op.Sql);
        Assert.Contains("COLLATE Japanese_CI_AS", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenColumnTypeWidens_EmitsAlterColumn()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "bigint", IsNullable = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AlterColumn, op.Kind);
        Assert.Contains("ALTER COLUMN", op.Sql);
        Assert.Contains("[Age]", op.Sql);
        Assert.Contains("bigint", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenColumnTypeNarrows_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "bigint", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Contains("not safe", skipped.Message);
    }

    [Fact]
    public void Diff_WhenNullabilityChanges_EmitsAlterColumn()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AlterColumn, op.Kind);
        Assert.Contains("NOT NULL", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenNotNullToNull_EmitsAlterColumn()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AlterColumn, op.Kind);
        Assert.EndsWith("NULL", op.Sql);
        Assert.DoesNotContain("NOT NULL", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenIdentityChanges_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, IsIdentity = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, IsIdentity = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Contains("IDENTITY", skipped.Message);
    }

    [Fact]
    public void Diff_WhenOnlyDefaultChanges_NoColumnSkip()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["STATUS"] = new ColumnModel { Name = "Status", SqlType = "int", IsNullable = false, DefaultExpression = "1" };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["STATUS"] = new ColumnModel { Name = "Status", SqlType = "int", IsNullable = false, DefaultExpression = "0" };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        // Column-level skip should NOT be emitted; DEFAULT change is handled by constraint diff
        Assert.DoesNotContain(plan.Skipped, s => s.Reason == SkippedReason.AlterNotSupported);
    }

    [Fact]
    public void Diff_WhenTypeWidensAndNullChanges_EmitsAlterColumn()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "bigint", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AlterColumn, op.Kind);
        Assert.Contains("bigint", op.Sql);
        Assert.Contains("NOT NULL", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenTypeFamilyChanges_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "varchar(100)", IsNullable = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["AGE"] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
    }

    [Fact]
    public void Diff_WhenTypeWidensButIdentityChanges_IsSkipped()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "bigint", IsNullable = false, IsIdentity = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, IsIdentity = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
        Assert.Contains("IDENTITY", skipped.Message);
    }

    [Fact]
    public void Diff_AlterColumnEmitsCorrectSqlSyntax()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(200)", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["NAME"] = new ColumnModel { Name = "Name", SqlType = "nvarchar(100)", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal("ALTER TABLE [dbo].[Users] ALTER COLUMN [Name] nvarchar(200) NOT NULL", op.Sql);
    }

    [Fact]
    public void Diff_AlterColumnWithSchema_EmitsBracketEscapedSchema()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("sales", "Orders");
        desiredTable.Columns["AMOUNT"] = new ColumnModel { Name = "Amount", SqlType = "decimal(18,4)", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("sales", "Orders");
        currentTable.Columns["AMOUNT"] = new ColumnModel { Name = "Amount", SqlType = "decimal(10,2)", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "sales" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal("ALTER TABLE [sales].[Orders] ALTER COLUMN [Amount] decimal(18,4) NOT NULL", op.Sql);
    }

    [Fact]
    public void Diff_AlterColumnOrder_AfterAddColumnBeforeConstraints()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "bigint", IsNullable = true };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("NewCol")] = new ColumnModel { Name = "NewCol", SqlType = "int", IsNullable = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Equal(2, plan.Operations.Count);
        Assert.Equal(OperationKind.AddColumn, plan.Operations[0].Kind);
        Assert.Equal(OperationKind.AlterColumn, plan.Operations[1].Kind);
    }

    [Fact]
    public void Diff_WhenNewFilteredIndex_EmitsWhereClause()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WHERE Id > 0";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "T").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateIndex, op.Kind);
        Assert.Contains("WHERE", op.Sql);
    }

    [Fact]
    public void Diff_WhenNewClusteredIndex_EmitsClusteredKeyword()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE CLUSTERED INDEX IX_T ON dbo.T (Id)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "T").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateIndex, op.Kind);
        Assert.Contains("CREATE CLUSTERED INDEX [IX_T]", op.Sql);
    }

    [Fact]
    public void Diff_WhenNewNonClusteredIndex_EmitsNonClusteredKeyword()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "T").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_T]", op.Sql);
    }

    [Fact]
    public void Diff_WhenTableAlreadyHasClusteredIndex_EmitsRecreateIndex()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL, Val int NOT NULL)\nCREATE CLUSTERED INDEX IX_T_Val ON dbo.T (Val)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns["VAL"] = new ColumnModel { Name = "Val", SqlType = "int", IsNullable = false };
        currentTable.Indexes["IX_T_ID"] = new IndexModel
        {
            Name = "IX_T_Id",
            IsUnique = false,
            IsClustered = true,
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Equal(2, plan.Operations.Count);

        // IX_T_Val: RecreateIndex (既存 IX_T_Id を DROP → IX_T_Val を CREATE)
        var recreateOp = plan.Operations[0];
        Assert.Equal(OperationKind.RecreateIndex, recreateOp.Kind);
        Assert.Contains("DROP INDEX [IX_T_Id]", recreateOp.Sql);
        Assert.Contains("CLUSTERED", recreateOp.Sql);
        Assert.Contains("IX_T_Val", recreateOp.Sql);

        // IX_T_Id: DropIndex (desired に存在しない既存インデックス)
        var dropOp = plan.Operations[1];
        Assert.Equal(OperationKind.DropIndex, dropOp.Kind);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenNewIndexHasFillFactor_EmitsWithClause()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WITH (FILLFACTOR = 80)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "T").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateIndex, op.Kind);
        Assert.Contains("WITH (FILLFACTOR = 80)", op.Sql);
    }

    [Fact]
    public void Diff_WhenDesiredIndexHasOnlineOption_ExistingHasNone_NoSkip()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WITH (ONLINE = ON)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Indexes["IX_T"] = new IndexModel
        {
            Name = "IX_T",
            IsUnique = false,
            IsClustered = false,
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
            Options = null,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        // ONLINE は比較対象外なので diff なし → 操作もスキップもない
        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenExistingIndexHasDifferentFilterPredicate_EmitsRecreateIndex()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WHERE Id > 0";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Indexes["IX_T"] = new IndexModel
        {
            Name = "IX_T",
            IsUnique = false,
            IsClustered = false,
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },

            // 現在のDBのフィルタ述語は desired と異なる
            FilterPredicate = "Id > 100",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateIndex, op.Kind);
        Assert.Contains("DROP INDEX", op.Sql);
        Assert.Contains("WHERE", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenExistingIndexHasSameFilterPredicate_IsNotSkipped()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WHERE Id > 0";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Indexes["IX_T"] = new IndexModel
        {
            Name = "IX_T",
            IsUnique = false,
            IsClustered = false,
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
            FilterPredicate = "Id > 0",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenExistingIndexHasDifferentFillFactor_EmitsRecreateIndex()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WITH (FILLFACTOR = 80)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "T");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Indexes["IX_T"] = new IndexModel
        {
            Name = "IX_T",
            IsUnique = false,
            IsClustered = false,
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
            Options = new System.Collections.Generic.Dictionary<string, string>
            {
                { "FILLFACTOR", "90" },
            },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateIndex, op.Kind);
        Assert.Contains("DROP INDEX", op.Sql);
        Assert.Contains("FILLFACTOR = 80", op.Sql);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenNewIndexHasMultipleOptions_EmitsAllOptionsInWithClause()
    {
        const string desiredSql =
            "CREATE TABLE dbo.T (Id int NOT NULL)\nCREATE INDEX IX_T ON dbo.T (Id) WITH (FILLFACTOR = 90, PAD_INDEX = ON)";
        var desired = DesiredSchemaLoader.Load(desiredSql);
        var current = new DatabaseModel();
        current.GetOrAddTable("dbo", "T").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.CreateIndex, op.Kind);
        Assert.Contains("FILLFACTOR = 90", op.Sql);
        Assert.Contains("PAD_INDEX = ON", op.Sql);
    }

    [Fact]
    public void Diff_WhenCurrentColumnHasUnsupportedFeature_SkipsAsUnsupportedFeatureInCurrent()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel
        {
            Name = "Id", SqlType = "int", IsNullable = false,
        };
        currentTable.Columns["AGE"] = new ColumnModel
        {
            Name = "Age", SqlType = "int", IsNullable = true,
            UnsupportedFeature = "ComputedColumn",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.UnsupportedFeatureInCurrent, skipped.Reason);
        Assert.Equal("Age", skipped.Target.Name);
        Assert.Contains("ComputedColumn", skipped.Message);
    }

    [Fact]
    public void Diff_WhenCurrentConstraintHasUnsupportedFeature_SkipsAsUnsupportedFeatureInCurrent()
    {
        const string desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL)
ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Other FOREIGN KEY (Id) REFERENCES dbo.Other(Id)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel
        {
            Name = "Id", SqlType = "int", IsNullable = false,
        };
        currentTable.Constraints[IdentifierHelper.NormalizeNameKey("FK_Users_Other")] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Other",
            Columns = new List<string> { "Id" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Other",
            ReferenceColumns = new List<string> { "Id" },
            UnsupportedFeature = "ForeignKeyReferenceSchema",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var skip = plan.Skipped.Single(s => s.Target.Name == "FK_Users_Other");
        Assert.Equal(SkippedReason.UnsupportedFeatureInCurrent, skip.Reason);
        Assert.Contains("ForeignKeyReferenceSchema", skip.Message);
    }

    [Fact]
    public void Diff_WhenCurrentIndexHasUnsupportedFeature_SkipsAsUnsupportedFeatureInCurrent()
    {
        const string desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL)
CREATE INDEX IX_Users_Id ON dbo.Users(Id)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel
        {
            Name = "Id", SqlType = "int", IsNullable = false,
        };
        currentTable.Indexes[IdentifierHelper.NormalizeNameKey("IX_Users_Id")] = new IndexModel
        {
            Name = "IX_Users_Id",
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
            UnsupportedFeature = "ColumnstoreIndex",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.UnsupportedFeatureInCurrent, skipped.Reason);
        Assert.Contains("ColumnstoreIndex", skipped.Message);
    }

    [Fact]
    public void Diff_WhenCurrentColumnIsComputed_DoesNotGenerateRebuildProposal()
    {
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NULL)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel
        {
            Name = "Id", SqlType = "int", IsNullable = false,
        };
        currentTable.Columns["AGE"] = new ColumnModel
        {
            Name = "Age", SqlType = "int", IsNullable = true,
            UnsupportedFeature = "ComputedColumn",
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Empty(plan.Proposals);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.UnsupportedFeatureInCurrent, skipped.Reason);
    }

    [Fact]
    public void Diff_DropColumn_EmitsCorrectSql()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns["LEGACY"] = new ColumnModel { Name = "Legacy", SqlType = "int", IsNullable = true };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.DropColumn, op.Kind);
        Assert.Equal("ALTER TABLE [dbo].[Users] DROP COLUMN [Legacy]", op.Sql);
        Assert.Equal("dbo.Users.Legacy", op.Target.ToDisplayName());
    }

    [Fact]
    public void Diff_DropConstraint_EmitsCorrectSql()
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

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.DropConstraint, op.Kind);
        Assert.Equal("ALTER TABLE [dbo].[Users] DROP CONSTRAINT [UQ_Users_Name]", op.Sql);
    }

    [Fact]
    public void Diff_DropForeignKey_EmitsCorrectSql()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Users");

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Constraints["FK_USERS_TEAMS"] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Users_Teams",
            Columns = new[] { "TeamId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Teams",
            ReferenceColumns = new[] { "Id" },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.DropForeignKey, op.Kind);
        Assert.Equal("ALTER TABLE [dbo].[Users] DROP CONSTRAINT [FK_Users_Teams]", op.Sql);
    }

    [Fact]
    public void Diff_DropIndex_EmitsCorrectSql()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Users");

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Indexes["IX_USERS_NAME"] = new IndexModel
        {
            Name = "IX_Users_Name",
            IsUnique = false,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Name" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.DropIndex, op.Kind);
        Assert.Equal("DROP INDEX [IX_Users_Name] ON [dbo].[Users]", op.Sql);
    }

    [Fact]
    public void Diff_DropDescription_Table_EmitsCorrectSql()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Description = "Old table desc";

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.DropDescription, op.Kind);
        Assert.Contains("sp_dropextendedproperty", op.Sql);
        Assert.Contains("MS_Description", op.Sql);
        Assert.DoesNotContain("COLUMN", op.Sql);
    }

    [Fact]
    public void Diff_DropDescription_Column_EmitsCorrectSql()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false, Description = "Primary key" };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.DropDescription, op.Kind);
        Assert.Contains("sp_dropextendedproperty", op.Sql);
        Assert.Contains("COLUMN", op.Sql);
        Assert.Contains("Id", op.Sql);
    }

    [Fact]
    public void Diff_DropOperationsOrderedAfterAddOperations()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Columns["EMAIL"] = new ColumnModel { Name = "Email", SqlType = "nvarchar(200)", IsNullable = true };
        desiredTable.Constraints["UQ_USERS_EMAIL"] = new ConstraintModel
        {
            Kind = ConstraintKind.Unique,
            Name = "UQ_Users_Email",
            Columns = new[] { "Email" },
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Columns["LEGACY"] = new ColumnModel { Name = "Legacy", SqlType = "int", IsNullable = true };
        currentTable.Indexes["IX_USERS_LEGACY"] = new IndexModel
        {
            Name = "IX_Users_Legacy",
            IsUnique = false,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Legacy" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var kinds = plan.Operations.Select(op => op.Kind).ToArray();
        Assert.Equal(new[]
        {
            OperationKind.AddColumn,
            OperationKind.AddConstraint,
            OperationKind.DropIndex,
            OperationKind.DropColumn,
        }, kinds);
    }

    [Fact]
    public void Diff_WithEmitProposals_DropOpsOnProposalTargetAreFilteredOut()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Legacy")] = new ColumnModel { Name = "Legacy", SqlType = "INT", IsNullable = true };
        currentTable.Indexes[IdentifierHelper.NormalizeNameKey("IX_Users_Legacy")] = new IndexModel
        {
            Name = "IX_Users_Legacy",
            IsUnique = false,
            KeyColumns = new[] { new IndexKeyColumn { Name = "Legacy" } },
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { EmitProposals = true };
        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Single(plan.Proposals);

        // Drop operations on the same table as the rebuild proposal should be filtered out
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Diff_WhenPkClusteringDiffers_IsSkippedAsAlterNotSupported()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
            IsClustered = false,
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
            IsClustered = true,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkippedReason.AlterNotSupported, skipped.Reason);
    }

    [Fact]
    public void Diff_WhenUniqueClusteringDiffers_EmitsRecreateConstraint()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Constraints["UQ_USERS_ID"] = new ConstraintModel
        {
            Kind = ConstraintKind.Unique,
            Name = "UQ_Users_Id",
            Columns = new[] { "Id" },
            IsClustered = true,
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Constraints["UQ_USERS_ID"] = new ConstraintModel
        {
            Kind = ConstraintKind.Unique,
            Name = "UQ_Users_Id",
            Columns = new[] { "Id" },
            IsClustered = false,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.RecreateConstraint, op.Kind);
        Assert.Contains("CLUSTERED", op.Sql);
    }

    [Fact]
    public void Diff_WhenPkSameClusteredState_NoOperation()
    {
        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desiredTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
            IsClustered = true,
        };

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
            IsClustered = true,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenNewPkConstraint_EmitsClusteredKeyword()
    {
        const string desiredSql = @"
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  CONSTRAINT PK_Users PRIMARY KEY NONCLUSTERED (Id)
)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var createOp = plan.Operations.First(op => op.Kind == OperationKind.CreateTable);
        Assert.Contains("CREATE TABLE", createOp.Sql);

        var constraintOp = plan.Operations.First(op => op.Kind == OperationKind.AddConstraint);
        Assert.Contains("PRIMARY KEY NONCLUSTERED", constraintOp.Sql);
    }

    [Fact]
    public void Diff_WhenDesiredPkClusteringIsUnspecified_DoesNotTreatCurrentAsDifferent()
    {
        const string desiredSql = @"
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  CONSTRAINT PK_Users PRIMARY KEY (Id)
)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        currentTable.Constraints["PK_USERS"] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
            IsClustered = false,
            IsClusteredSpecified = true,
        };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WhenAddingPkWithUnspecifiedClustering_OmitsClusteredKeyword()
    {
        const string desiredSql = @"
CREATE TABLE dbo.Users (
  Id int NOT NULL,
  CONSTRAINT PK_Users PRIMARY KEY (Id)
)";
        var desired = DesiredSchemaLoader.Load(desiredSql);

        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var plan = SchemaDiffer.Diff(current, desired, metadata);

        var op = Assert.Single(plan.Operations);
        Assert.Equal(OperationKind.AddConstraint, op.Kind);
        Assert.DoesNotContain("CLUSTERED", op.Sql);
        Assert.DoesNotContain("NONCLUSTERED", op.Sql);
    }
}
