using System;
using SqlSchemaDef.Core.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class MigrationPlanJsonTests
{
    [Fact]
    public void ToJson_EmptyPlan_ProducesValidJson()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>());

        var json = MigrationPlanSerializer.ToJson(plan);

        Assert.Contains("\"planFormatVersion\": 1", json);
        Assert.Contains("\"operations\": []", json);
        Assert.Contains("\"skipped\": []", json);
    }

    [Fact]
    public void ToJson_WithOperationsAndSkipped_ProducesCompleteJson()
    {
        var plan = CreateSamplePlan();

        var json = MigrationPlanSerializer.ToJson(plan);

        Assert.Contains("CREATE TABLE dbo.Users", json);
        Assert.Contains("createTable", json);
        Assert.Contains("dropNotSupported", json);
        Assert.Contains("OldTable", json);
    }

    [Fact]
    public void ToJson_IsDeterministic_GivenSamePlan()
    {
        var plan = CreateSamplePlan();
        var json1 = MigrationPlanSerializer.ToJson(plan);
        var json2 = MigrationPlanSerializer.ToJson(plan);
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void ToJson_OutputIsIndented()
    {
        var plan = CreateSamplePlan();
        var json = MigrationPlanSerializer.ToJson(plan);
        Assert.Contains("\n", json);
    }

    [Fact]
    public void ToJson_NullPlan_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MigrationPlanSerializer.ToJson(null));
    }

    [Fact]
    public void FromJson_ValidJson_ProducesMigrationPlan()
    {
        var plan = CreateSamplePlan();
        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.Equal(plan.Metadata.PlanFormatVersion, deserialized.Metadata.PlanFormatVersion);
        Assert.Equal(plan.Metadata.Schema, deserialized.Metadata.Schema);
        Assert.Equal(plan.Operations.Count, deserialized.Operations.Count);
        Assert.Equal(plan.Skipped.Count, deserialized.Skipped.Count);

        Assert.Equal(plan.Operations[0].Sql, deserialized.Operations[0].Sql);
        Assert.Equal(plan.Operations[0].Kind, deserialized.Operations[0].Kind);
        Assert.Equal(plan.Operations[0].Description, deserialized.Operations[0].Description);
        Assert.Equal(plan.Operations[0].Target.Name, deserialized.Operations[0].Target.Name);

        Assert.Equal(plan.Skipped[0].Reason, deserialized.Skipped[0].Reason);
        Assert.Equal(plan.Skipped[0].Message, deserialized.Skipped[0].Message);
        Assert.Equal(plan.Skipped[0].Target.Name, deserialized.Skipped[0].Target.Name);
    }

    [Fact]
    public void FromJson_UnknownFormatVersion_ThrowsInformativeException()
    {
        const string json = @"{""metadata"":{""planFormatVersion"":99},""operations"":[],""skipped"":[]}";
        var ex = Assert.Throws<InvalidOperationException>(() => MigrationPlanSerializer.FromJson(json));
        Assert.Contains("99", ex.Message);
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromJson_NullJson_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MigrationPlanSerializer.FromJson(null));
    }

    [Fact]
    public void FromJson_EmptyJson_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => MigrationPlanSerializer.FromJson(string.Empty));
    }

    [Fact]
    public void RoundTrip_PreservesAllOperationKinds()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            new[]
            {
                new SqlOperation { Kind = OperationKind.CreateTable, Sql = "S1", Description = "D1" },
                new SqlOperation { Kind = OperationKind.AddColumn, Sql = "S2", Description = "D2" },
                new SqlOperation { Kind = OperationKind.AlterColumn, Sql = "S2b", Description = "D2b" },
                new SqlOperation { Kind = OperationKind.AddConstraint, Sql = "S3", Description = "D3" },
                new SqlOperation { Kind = OperationKind.CreateIndex, Sql = "S4", Description = "D4" },
                new SqlOperation { Kind = OperationKind.AddForeignKey, Sql = "S5", Description = "D5" },
                new SqlOperation { Kind = OperationKind.RecreateConstraint, Sql = "S6", Description = "D6" },
                new SqlOperation { Kind = OperationKind.RecreateIndex, Sql = "S7", Description = "D7" },
                new SqlOperation { Kind = OperationKind.RecreateForeignKey, Sql = "S8", Description = "D8" },
                new SqlOperation { Kind = OperationKind.AddDescription, Sql = "S9", Description = "D9" },
                new SqlOperation { Kind = OperationKind.UpdateDescription, Sql = "S10", Description = "D10" },
                new SqlOperation { Kind = OperationKind.DropDescription, Sql = "S11", Description = "D11" },
                new SqlOperation { Kind = OperationKind.DropForeignKey, Sql = "S12", Description = "D12" },
                new SqlOperation { Kind = OperationKind.DropIndex, Sql = "S13", Description = "D13" },
                new SqlOperation { Kind = OperationKind.DropConstraint, Sql = "S14", Description = "D14" },
                new SqlOperation { Kind = OperationKind.DropColumn, Sql = "S15", Description = "D15" },
                new SqlOperation { Kind = OperationKind.DropTable, Sql = "S16", Description = "D16" },
            },
            Array.Empty<SkippedItem>());

        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.Equal(17, deserialized.Operations.Count);
        Assert.Equal(OperationKind.CreateTable, deserialized.Operations[0].Kind);
        Assert.Equal(OperationKind.AddColumn, deserialized.Operations[1].Kind);
        Assert.Equal(OperationKind.AlterColumn, deserialized.Operations[2].Kind);
        Assert.Equal(OperationKind.AddConstraint, deserialized.Operations[3].Kind);
        Assert.Equal(OperationKind.CreateIndex, deserialized.Operations[4].Kind);
        Assert.Equal(OperationKind.AddForeignKey, deserialized.Operations[5].Kind);
        Assert.Equal(OperationKind.RecreateConstraint, deserialized.Operations[6].Kind);
        Assert.Equal(OperationKind.RecreateIndex, deserialized.Operations[7].Kind);
        Assert.Equal(OperationKind.RecreateForeignKey, deserialized.Operations[8].Kind);
        Assert.Equal(OperationKind.AddDescription, deserialized.Operations[9].Kind);
        Assert.Equal(OperationKind.UpdateDescription, deserialized.Operations[10].Kind);
        Assert.Equal(OperationKind.DropDescription, deserialized.Operations[11].Kind);
        Assert.Equal(OperationKind.DropForeignKey, deserialized.Operations[12].Kind);
        Assert.Equal(OperationKind.DropIndex, deserialized.Operations[13].Kind);
        Assert.Equal(OperationKind.DropConstraint, deserialized.Operations[14].Kind);
        Assert.Equal(OperationKind.DropColumn, deserialized.Operations[15].Kind);
        Assert.Equal(OperationKind.DropTable, deserialized.Operations[16].Kind);
    }

    [Fact]
    public void RoundTrip_PreservesAllSkippedReasons()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            Array.Empty<SqlOperation>(),
            new[]
            {
                new SkippedItem { Reason = SkippedReason.DropNotSupported, Message = "m1" },
                new SkippedItem { Reason = SkippedReason.AlterNotSupported, Message = "m2" },
                new SkippedItem { Reason = SkippedReason.NotNullAddNotSupported, Message = "m3" },
                new SkippedItem { Reason = SkippedReason.UnsupportedFeatureInDesired, Message = "m4" },
                new SkippedItem { Reason = SkippedReason.UnsupportedFeatureInCurrent, Message = "m5" },
            });

        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.Equal(5, deserialized.Skipped.Count);
        Assert.Equal(SkippedReason.DropNotSupported, deserialized.Skipped[0].Reason);
        Assert.Equal(SkippedReason.AlterNotSupported, deserialized.Skipped[1].Reason);
        Assert.Equal(SkippedReason.NotNullAddNotSupported, deserialized.Skipped[2].Reason);
        Assert.Equal(SkippedReason.UnsupportedFeatureInDesired, deserialized.Skipped[3].Reason);
        Assert.Equal(SkippedReason.UnsupportedFeatureInCurrent, deserialized.Skipped[4].Reason);
    }

    [Fact]
    public void PlanMetadata_PlanFormatVersion_DefaultsToOne()
    {
        var metadata = new PlanMetadata();
        Assert.Equal(1, metadata.PlanFormatVersion);
    }

    [Fact]
    public void ToJson_WithProposals_IncludesProposalsArray()
    {
        var plan = CreatePlanWithProposals();
        var json = MigrationPlanSerializer.ToJson(plan);

        Assert.Contains("\"proposals\"", json);
        Assert.Contains("\"createShadowTable\"", json);
        Assert.Contains("__Users_rebuild", json);
    }

    [Fact]
    public void ToJson_WithoutProposals_OmitsProposalsKey()
    {
        var plan = CreateSamplePlan();
        var json = MigrationPlanSerializer.ToJson(plan);

        Assert.DoesNotContain("\"proposals\"", json);
    }

    [Fact]
    public void FromJson_WithProposals_DeserializesCorrectly()
    {
        var plan = CreatePlanWithProposals();
        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.Equal(1, deserialized.Proposals.Count);
        Assert.Equal("Rebuild dbo.Users", deserialized.Proposals[0].Description);
        Assert.Equal("dbo", deserialized.Proposals[0].Target.Schema);
        Assert.Equal("Users", deserialized.Proposals[0].Target.Name);
        Assert.Equal(2, deserialized.Proposals[0].Steps.Count);
        Assert.Equal(RebuildStepKind.CreateShadowTable, deserialized.Proposals[0].Steps[0].Kind);
        Assert.Equal(RebuildStepKind.CopyData, deserialized.Proposals[0].Steps[1].Kind);
    }

    [Fact]
    public void FromJson_WithoutProposalsKey_DefaultsToEmpty()
    {
        const string json = @"{""metadata"":{""planFormatVersion"":1},""operations"":[],""skipped"":[]}";
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.NotNull(deserialized.Proposals);
        Assert.Empty(deserialized.Proposals);
    }

    [Fact]
    public void RoundTrip_PreservesProposalSteps()
    {
        var plan = CreatePlanWithProposals();
        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        var original = plan.Proposals[0];
        var roundTripped = deserialized.Proposals[0];

        Assert.Equal(original.Steps.Count, roundTripped.Steps.Count);
        for (int i = 0; i < original.Steps.Count; i++)
        {
            Assert.Equal(original.Steps[i].Kind, roundTripped.Steps[i].Kind);
            Assert.Equal(original.Steps[i].Description, roundTripped.Steps[i].Description);
            Assert.Equal(original.Steps[i].Sql, roundTripped.Steps[i].Sql);
        }

        Assert.Equal(original.Script, roundTripped.Script);
        Assert.Equal(original.Warning, roundTripped.Warning);
    }

    [Fact]
    public void RoundTrip_SkippedItemWithDetails_PreservesDetails()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            Array.Empty<SqlOperation>(),
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.AlterNotSupported,
                    Message = "alter is not supported in v1",
                    Target = new SqlObjectRef { Type = SqlObjectType.Column, Schema = "dbo", ParentName = "Users", Name = "Age" },
                    Details = "desired=bigint current=int",
                },
            });

        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.Equal("desired=bigint current=int", deserialized.Skipped[0].Details);
    }

    [Fact]
    public void ToJson_SkippedItemWithNullDetails_OmitsDetailsKey()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            Array.Empty<SqlOperation>(),
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.DropNotSupported,
                    Message = "drop is not supported",
                    Details = null,
                },
            });

        var json = MigrationPlanSerializer.ToJson(plan);

        // null は出力されない (nulls ignored 設定)
        Assert.DoesNotContain("\"details\"", json);
    }

    private static MigrationPlan CreatePlanWithProposals()
    {
        return new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1, DatabaseName = "TestDb" },
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
                    Description = "Rebuild dbo.Users",
                    Warning = "FK from dbo.Orders references this table",
                    Steps = new[]
                    {
                        new RebuildStep { Kind = RebuildStepKind.CreateShadowTable, Description = "Create shadow table", Sql = "CREATE TABLE dbo.__Users_rebuild (Id INT NOT NULL, Age BIGINT NULL)" },
                        new RebuildStep { Kind = RebuildStepKind.CopyData, Description = "Copy data", Sql = "INSERT INTO dbo.__Users_rebuild (Id, Age) SELECT Id, Age FROM dbo.Users" },
                    },
                    Script = "CREATE TABLE dbo.__Users_rebuild (Id INT NOT NULL, Age BIGINT NULL)\nGO\nINSERT INTO dbo.__Users_rebuild (Id, Age) SELECT Id, Age FROM dbo.Users",
                },
            });
    }

    private static MigrationPlan CreateSamplePlan()
    {
        return new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1, DatabaseName = "TestDb" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Sql = "CREATE TABLE dbo.Users (Id int NOT NULL)",
                    Description = "Create table dbo.Users",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                },
            },
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.DropNotSupported,
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "OldTable" },
                    Message = "drop is not supported in v1",
                },
            });
    }

    [Fact]
    public void RoundTrip_ColumnReorderRequired_PreservesReason()
    {
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            Array.Empty<SqlOperation>(),
            new[]
            {
                new SkippedItem
                {
                    Reason = SkippedReason.ColumnReorderRequired,
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
                    Message = "column order differs; requires rebuild",
                },
            });

        var json = MigrationPlanSerializer.ToJson(plan);
        var restored = MigrationPlanSerializer.FromJson(json);

        var skip = Assert.Single(restored.Skipped);
        Assert.Equal(SkippedReason.ColumnReorderRequired, skip.Reason);
        Assert.Contains("columnReorderRequired", json);
    }
}
