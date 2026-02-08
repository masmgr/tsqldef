using SqlSchemaDef.Core.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class MigrationPlanToScriptTests
{
    [Fact]
    public void ToScript_EmptyPlan_PrintsHeaderOnly()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>());

        var script = plan.ToScript(new ScriptOptions { HeaderMode = ScriptHeaderMode.DryRunStyle, NewLine = "\n" });

        Assert.Contains("-- SqlSchemaDef plan", script);
        Assert.Contains("-- Operations: 0", script);
    }

    [Fact]
    public void ToScript_IncludesOperationsAndSkipped_Deterministically()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Create table dbo.Users",
                    Sql = "CREATE TABLE dbo.Users (Id int NOT NULL)",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                },
            },
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.DropNotSupported,
                    Target = new SqlObjectRef { Type = SqlObjectType.Index, Schema = "dbo", ParentName = "Users", Name = "IX_Users_Old" },
                    Message = "drop is not supported in v1",
                },
            });

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\n",
            TerminateWithSemicolon = true,
            IncludeSkipped = true,
        });

        Assert.Contains("CREATE TABLE dbo.Users (Id int NOT NULL);", script);
        Assert.Contains("-- Skipped: DropNotSupported dbo.Users.IX_Users_Old - drop is not supported in v1\n", script);
    }

    [Fact]
    public void ToScript_DoesNotTerminate_WhenOptionDisabled()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Create table dbo.Users",
                    Sql = "CREATE TABLE dbo.Users (Id int NOT NULL)",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                },
            },
            Array.Empty<SkippedItem>());

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\n",
            TerminateWithSemicolon = false,
        });

        Assert.Contains("CREATE TABLE dbo.Users (Id int NOT NULL)\n", script);
        Assert.DoesNotContain("CREATE TABLE dbo.Users (Id int NOT NULL);\n", script);
    }

    [Fact]
    public void ToScript_ExcludeSkipped_WhenOptionDisabled()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.DropNotSupported,
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                    Message = "drop is not supported in v1",
                },
            });

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\n",
            IncludeSkipped = false,
        });

        Assert.DoesNotContain("-- Skipped:", script);
    }

    [Fact]
    public void MigrationPlan_ConstructorDefaultsProposalsToEmpty()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>());

        Assert.NotNull(plan.Proposals);
        Assert.Empty(plan.Proposals);
    }

    [Fact]
    public void ToScript_WithProposals_WhenIncludeProposalsFalse_OmitsProposals()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>(),
            new[]
            {
                new RebuildProposal
                {
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                    Description = "Rebuild dbo.Users",
                    Steps = new[]
                    {
                        new RebuildStep { Kind = RebuildStepKind.CreateShadowTable, Description = "Create shadow table", Sql = "CREATE TABLE dbo.__Users_rebuild (Id INT NOT NULL)" },
                    },
                    Script = "CREATE TABLE dbo.__Users_rebuild (Id INT NOT NULL)",
                },
            });

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\n",
            IncludeProposals = false,
        });

        Assert.DoesNotContain("PROPOSAL", script);
        Assert.DoesNotContain("__Users_rebuild", script);
    }

    [Fact]
    public void ToScript_WithProposals_WhenIncludeProposalsTrue_AppendsProposalSection()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.AlterNotSupported,
                    Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
                    Message = "alter is not supported in v1",
                },
            },
            new[]
            {
                new RebuildProposal
                {
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                    Description = "Rebuild dbo.Users (column type change: Age)",
                    Steps = new[]
                    {
                        new RebuildStep { Kind = RebuildStepKind.CreateShadowTable, Description = "Create shadow table", Sql = "CREATE TABLE dbo.__Users_rebuild (Id INT NOT NULL, Age BIGINT NULL)" },
                        new RebuildStep { Kind = RebuildStepKind.CopyData, Description = "Copy data from original", Sql = "INSERT INTO dbo.__Users_rebuild (Id, Age) SELECT Id, Age FROM dbo.Users" },
                        new RebuildStep { Kind = RebuildStepKind.RenameOriginalToOld, Description = "Rename original to _old", Sql = "EXEC sp_rename 'dbo.Users', 'Users_old'" },
                        new RebuildStep { Kind = RebuildStepKind.RenameShadowToOriginal, Description = "Rename shadow to original", Sql = "EXEC sp_rename 'dbo.__Users_rebuild', 'Users'" },
                        new RebuildStep { Kind = RebuildStepKind.DropOldTable, Description = "Drop old table", Sql = "DROP TABLE dbo.Users_old" },
                    },
                    Script = "CREATE TABLE ...",
                },
            });

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\n",
            IncludeSkipped = true,
            IncludeProposals = true,
        });

        Assert.Contains("-- PROPOSALS (review only - NOT applied automatically)", script);
        Assert.Contains("-- Proposal: Rebuild dbo.Users (column type change: Age)", script);
        Assert.Contains("-- Step 1: Create shadow table", script);
        Assert.Contains("-- CREATE TABLE dbo.__Users_rebuild (Id INT NOT NULL, Age BIGINT NULL)", script);
        Assert.Contains("-- Step 2: Copy data from original", script);
        Assert.Contains("-- Step 3: Rename original to _old", script);
        Assert.Contains("-- Step 4: Rename shadow to original", script);
        Assert.Contains("-- Step 5: Drop old table", script);
        Assert.Contains("-- DROP TABLE dbo.Users_old", script);
    }

    [Fact]
    public void ToScript_WithEmptyProposals_NoProposalSection()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>(),
            Array.Empty<RebuildProposal>());

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\n",
            IncludeProposals = true,
        });

        Assert.DoesNotContain("PROPOSAL", script);
    }

    [Fact]
    public void ToScript_UsesCustomNewLine()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Create table dbo.Users",
                    Sql = "CREATE TABLE dbo.Users (Id int NOT NULL)",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                },
            },
            Array.Empty<SkippedItem>());

        var script = plan.ToScript(new ScriptOptions
        {
            HeaderMode = ScriptHeaderMode.None,
            NewLine = "\r\n",
            TerminateWithSemicolon = true,
        });

        Assert.Contains("CREATE TABLE dbo.Users (Id int NOT NULL);\r\n", script);
        Assert.DoesNotContain("CREATE TABLE dbo.Users (Id int NOT NULL);\n", script);
    }
}
