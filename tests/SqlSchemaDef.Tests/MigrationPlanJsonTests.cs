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
        var json = @"{""metadata"":{""planFormatVersion"":99},""operations"":[],""skipped"":[]}";
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
                new SqlOperation { Kind = OperationKind.AddConstraint, Sql = "S3", Description = "D3" },
                new SqlOperation { Kind = OperationKind.CreateIndex, Sql = "S4", Description = "D4" },
                new SqlOperation { Kind = OperationKind.AddForeignKey, Sql = "S5", Description = "D5" },
            },
            Array.Empty<SkippedItem>());

        var json = MigrationPlanSerializer.ToJson(plan);
        var deserialized = MigrationPlanSerializer.FromJson(json);

        Assert.Equal(5, deserialized.Operations.Count);
        Assert.Equal(OperationKind.CreateTable, deserialized.Operations[0].Kind);
        Assert.Equal(OperationKind.AddColumn, deserialized.Operations[1].Kind);
        Assert.Equal(OperationKind.AddConstraint, deserialized.Operations[2].Kind);
        Assert.Equal(OperationKind.CreateIndex, deserialized.Operations[3].Kind);
        Assert.Equal(OperationKind.AddForeignKey, deserialized.Operations[4].Kind);
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
}
