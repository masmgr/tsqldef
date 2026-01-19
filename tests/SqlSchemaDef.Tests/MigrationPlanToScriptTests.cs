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
