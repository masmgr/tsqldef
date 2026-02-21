using System;
using System.Collections.Generic;
using System.Linq;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class RebuildProposalBuilderTests
{
    private static readonly PlanMetadata DefaultMetadata = new PlanMetadata { Schema = "dbo" };

    [Fact]
    public void BuildProposals_WhenNoAlterSkipped_ReturnsEmpty()
    {
        var current = new DatabaseModel();
        var desired = new DatabaseModel();
        var skipped = Array.Empty<SkippedItem>();

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        Assert.Empty(proposals);
    }

    [Fact]
    public void BuildProposals_WhenColumnTypeDiffers_ReturnsProposal()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true) });

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
                Message = "alter is not supported in v1",
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        Assert.Single(proposals);
        Assert.Equal("Users", proposals[0].Target.Name);
        Assert.Contains("Age", proposals[0].Description);
    }

    [Fact]
    public void BuildProposals_ProposalHasCorrectStepOrder()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true) });

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var steps = proposals[0].Steps;

        Assert.Equal(RebuildStepKind.CreateShadowTable, steps[0].Kind);
        Assert.Equal(RebuildStepKind.CopyData, steps[1].Kind);
        Assert.Equal(RebuildStepKind.RenameOriginalToOld, steps[2].Kind);
        Assert.Equal(RebuildStepKind.RenameShadowToOriginal, steps[3].Kind);
        Assert.Equal(RebuildStepKind.DropOldTable, steps[4].Kind);
    }

    [Fact]
    public void BuildProposals_CreateShadowTable_UsesDesiredColumns()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true) });

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var createStep = proposals[0].Steps.First(s => s.Kind == RebuildStepKind.CreateShadowTable);

        Assert.Contains("__Users_rebuild", createStep.Sql);
        Assert.Contains("BIGINT", createStep.Sql);
    }

    [Fact]
    public void BuildProposals_CopyData_UsesColumnIntersection()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true), ("Deleted", "BIT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true), ("Email", "NVARCHAR(200)", true) });

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var copyStep = proposals[0].Steps.First(s => s.Kind == RebuildStepKind.CopyData);

        // Id and Age are common columns; Deleted is only in current, Email is only in desired
        Assert.Contains("Age", copyStep.Sql);
        Assert.Contains("Id", copyStep.Sql);
        Assert.DoesNotContain("Deleted", copyStep.Sql);
        Assert.DoesNotContain("Email", copyStep.Sql);
    }

    [Fact]
    public void BuildProposals_CopyData_WrapsIdentityInsert()
    {
        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false, IsIdentity = true };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };

        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false, IsIdentity = true };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var copyStep = proposals[0].Steps.First(s => s.Kind == RebuildStepKind.CopyData);

        Assert.Contains("SET IDENTITY_INSERT", copyStep.Sql);
        Assert.Contains("ON", copyStep.Sql);
        Assert.Contains("OFF", copyStep.Sql);
    }

    [Fact]
    public void BuildProposals_RecreateConstraints_UsesDesiredConstraints()
    {
        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };
        currentTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
        };

        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };
        desiredTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
        };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var steps = proposals[0].Steps;

        Assert.Contains(steps, s => s.Kind == RebuildStepKind.DropConstraintsOnOriginal);
        Assert.Contains(steps, s => s.Kind == RebuildStepKind.RecreateConstraints);

        var recreateStep = steps.First(s => s.Kind == RebuildStepKind.RecreateConstraints);
        Assert.Contains("PK_Users", recreateStep.Sql);
        Assert.Contains("PRIMARY KEY", recreateStep.Sql);
    }

    [Fact]
    public void BuildProposals_RecreateIndexes_UsesDesiredIndexes()
    {
        var current = new DatabaseModel();
        var currentTable = current.GetOrAddTable("dbo", "Users");
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        currentTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };
        currentTable.Indexes[IdentifierHelper.NormalizeNameKey("IX_Users_Age")] = new IndexModel
        {
            Name = "IX_Users_Age",
            KeyColumns = new[] { new IndexKeyColumn { Name = "Age" } },
        };

        var desired = new DatabaseModel();
        var desiredTable = desired.GetOrAddTable("dbo", "Users");
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        desiredTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };
        desiredTable.Indexes[IdentifierHelper.NormalizeNameKey("IX_Users_Age")] = new IndexModel
        {
            Name = "IX_Users_Age",
            KeyColumns = new[] { new IndexKeyColumn { Name = "Age" } },
        };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var recreateStep = proposals[0].Steps.First(s => s.Kind == RebuildStepKind.RecreateIndexes);

        Assert.Contains("IX_Users_Age", recreateStep.Sql);
        Assert.Contains("NONCLUSTERED INDEX", recreateStep.Sql);
    }

    [Fact]
    public void BuildProposals_WhenFkFromOtherTableReferencesTarget_EmitsWarning()
    {
        var current = new DatabaseModel();
        var usersTable = current.GetOrAddTable("dbo", "Users");
        usersTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        usersTable.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "INT", IsNullable = true };

        var ordersTable = current.GetOrAddTable("dbo", "Orders");
        ordersTable.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        ordersTable.Columns[IdentifierHelper.NormalizeNameKey("UserId")] = new ColumnModel { Name = "UserId", SqlType = "INT", IsNullable = false };
        ordersTable.Constraints[IdentifierHelper.NormalizeNameKey("FK_Orders_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.ForeignKey,
            Name = "FK_Orders_Users",
            Columns = new[] { "UserId" },
            ReferenceSchema = "dbo",
            ReferenceTable = "Users",
            ReferenceColumns = new[] { "Id" },
        };

        var desired = new DatabaseModel();
        var desiredUsers = desired.GetOrAddTable("dbo", "Users");
        desiredUsers.Columns[IdentifierHelper.NormalizeNameKey("Id")] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        desiredUsers.Columns[IdentifierHelper.NormalizeNameKey("Age")] = new ColumnModel { Name = "Age", SqlType = "BIGINT", IsNullable = true };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        Assert.NotNull(proposals[0].Warning);
        Assert.Contains("FK_Orders_Users", proposals[0].Warning);
        Assert.Contains("WARNING", proposals[0].Warning);
    }

    [Fact]
    public void BuildProposals_MultipleTables_ProducesMultipleProposals()
    {
        var current = new DatabaseModel();
        var t1 = current.GetOrAddTable("dbo", "A");
        t1.Columns[IdentifierHelper.NormalizeNameKey("Col")] = new ColumnModel { Name = "Col", SqlType = "INT", IsNullable = true };
        var t2 = current.GetOrAddTable("dbo", "B");
        t2.Columns[IdentifierHelper.NormalizeNameKey("Col")] = new ColumnModel { Name = "Col", SqlType = "INT", IsNullable = true };

        var desired = new DatabaseModel();
        var dt1 = desired.GetOrAddTable("dbo", "A");
        dt1.Columns[IdentifierHelper.NormalizeNameKey("Col")] = new ColumnModel { Name = "Col", SqlType = "BIGINT", IsNullable = true };
        var dt2 = desired.GetOrAddTable("dbo", "B");
        dt2.Columns[IdentifierHelper.NormalizeNameKey("Col")] = new ColumnModel { Name = "Col", SqlType = "BIGINT", IsNullable = true };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "A", Name = "Col" },
            },
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "B", Name = "Col" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        Assert.Equal(2, proposals.Count);
    }

    [Fact]
    public void BuildProposals_PrimaryKeyAlterSkipped_ProducesProposal()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Name", "NVARCHAR(100)", false) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Name", "NVARCHAR(100)", false) });

        var desiredTable = desired.Tables[IdentifierHelper.BuildTableKey("dbo", "Users")];
        desiredTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
        };

        var currentTable = current.Tables[IdentifierHelper.BuildTableKey("dbo", "Users")];
        currentTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id", "Name" },
        };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Constraint, Schema = "dbo", ParentName = "Users", Name = "PK_Users" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        var proposal = Assert.Single(proposals);
        Assert.Equal("Users", proposal.Target.Name);
        Assert.Contains("primary key change", proposal.Description);
        Assert.Contains("PK_Users", proposal.Description);
    }

    [Fact]
    public void BuildProposals_ColumnAndPkOnSameTable_SingleProposal()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true) });

        var desiredTable = desired.Tables[IdentifierHelper.BuildTableKey("dbo", "Users")];
        desiredTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id" },
        };

        var currentTable = current.Tables[IdentifierHelper.BuildTableKey("dbo", "Users")];
        currentTable.Constraints[IdentifierHelper.NormalizeNameKey("PK_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.PrimaryKey,
            Name = "PK_Users",
            Columns = new[] { "Id", "Age" },
        };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Constraint, Schema = "dbo", ParentName = "Users", Name = "PK_Users" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        var proposal = Assert.Single(proposals);
        Assert.Contains("column change", proposal.Description);
        Assert.Contains("primary key change", proposal.Description);
    }

    [Fact]
    public void BuildProposals_NonPkConstraintSkipped_DoesNotProduceProposal()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false) });

        // desired には UNIQUE 制約を追加（PK ではない）
        var desiredTable = desired.Tables[IdentifierHelper.BuildTableKey("dbo", "Users")];
        desiredTable.Constraints[IdentifierHelper.NormalizeNameKey("UQ_Users")] = new ConstraintModel
        {
            Kind = ConstraintKind.Unique,
            Name = "UQ_Users",
            Columns = new[] { "Id" },
        };

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Constraint, Schema = "dbo", ParentName = "Users", Name = "UQ_Users" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        Assert.Empty(proposals);
    }

    [Fact]
    public void BuildProposals_DropNotSupported_DoesNotTriggerProposal()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false) });
        var desired = new DatabaseModel();

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.DropNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Id" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);

        Assert.Empty(proposals);
    }

    [Fact]
    public void BuildProposals_Script_ConcatenatesAllSteps()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true) });

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var script = proposals[0].Script;

        Assert.Contains("GO", script);
        Assert.Contains("CREATE TABLE", script);
        Assert.Contains("INSERT INTO", script);
        Assert.Contains("sp_rename", script);
        Assert.Contains("DROP TABLE", script);
    }

    [Fact]
    public void BuildProposals_RenameSteps_UseCorrectNamingConvention()
    {
        var current = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "INT", true) });
        var desired = BuildModel("dbo", "Users", new[] { ("Id", "INT", false), ("Age", "BIGINT", true) });

        var skipped = new[]
        {
            new SkippedItem
            {
                Reason = SkippedReason.AlterNotSupported,
                Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
            },
        };

        var proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
        var steps = proposals[0].Steps;

        var renameOld = steps.First(s => s.Kind == RebuildStepKind.RenameOriginalToOld);
        Assert.Contains("'[dbo].[Users]'", renameOld.Sql);
        Assert.Contains("'Users_old'", renameOld.Sql);

        var renameShadow = steps.First(s => s.Kind == RebuildStepKind.RenameShadowToOriginal);
        Assert.Contains("'[dbo].[__Users_rebuild]'", renameShadow.Sql);
        Assert.Contains("'Users'", renameShadow.Sql);

        var dropOld = steps.First(s => s.Kind == RebuildStepKind.DropOldTable);
        Assert.Contains("[dbo].[Users_old]", dropOld.Sql);
    }

    private static DatabaseModel BuildModel(string schema, string tableName, (string Name, string Type, bool Nullable)[] columns)
    {
        var model = new DatabaseModel();
        var table = model.GetOrAddTable(schema, tableName);
        foreach (var (name, type, nullable) in columns)
        {
            table.Columns[IdentifierHelper.NormalizeNameKey(name)] = new ColumnModel
            {
                Name = name,
                SqlType = type,
                IsNullable = nullable,
            };
        }

        return model;
    }
}
