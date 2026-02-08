using FsCheck;
using FsCheck.Xunit;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.Tests.Properties.Generators;

namespace SqlSchemaDef.Tests.Properties;

public sealed class MigrationPlanJsonPropertyTests
{
    [Property(MaxTest = 100)]
    public Property RoundTrip_ToJson_FromJson_PreservesOperationCount()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            var json = MigrationPlanSerializer.ToJson(plan);
            var deserialized = MigrationPlanSerializer.FromJson(json);

            return (deserialized.Operations.Count == plan.Operations.Count)
                .Label($"ops: expected={plan.Operations.Count} actual={deserialized.Operations.Count}");
        });
    }

    [Property(MaxTest = 100)]
    public Property RoundTrip_ToJson_FromJson_PreservesSkippedCount()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            var json = MigrationPlanSerializer.ToJson(plan);
            var deserialized = MigrationPlanSerializer.FromJson(json);

            return (deserialized.Skipped.Count == plan.Skipped.Count)
                .Label($"skipped: expected={plan.Skipped.Count} actual={deserialized.Skipped.Count}");
        });
    }

    [Property(MaxTest = 100)]
    public Property RoundTrip_ToJson_FromJson_PreservesOperationSql()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            var json = MigrationPlanSerializer.ToJson(plan);
            var deserialized = MigrationPlanSerializer.FromJson(json);

            if (plan.Operations.Count != deserialized.Operations.Count)
                return false.Label("Count mismatch");

            for (int i = 0; i < plan.Operations.Count; i++)
            {
                if (plan.Operations[i].Sql != deserialized.Operations[i].Sql)
                    return false.Label($"Sql differs at index {i}");
                if (plan.Operations[i].Kind != deserialized.Operations[i].Kind)
                    return false.Label($"Kind differs at index {i}");
            }

            return true.ToProperty();
        });
    }

    [Property(MaxTest = 100)]
    public Property RoundTrip_ToJson_FromJson_PreservesMetadata()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            var json = MigrationPlanSerializer.ToJson(plan);
            var deserialized = MigrationPlanSerializer.FromJson(json);

            return (deserialized.Metadata.Schema == plan.Metadata.Schema &&
                    deserialized.Metadata.PlanFormatVersion == plan.Metadata.PlanFormatVersion)
                .Label($"schema: expected={plan.Metadata.Schema} actual={deserialized.Metadata.Schema}");
        });
    }

    [Property(MaxTest = 100)]
    public Property ToJson_IsDeterministic()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            var json1 = MigrationPlanSerializer.ToJson(plan);
            var json2 = MigrationPlanSerializer.ToJson(plan);
            return (json1 == json2)
                .Label("JSON output differs across calls");
        });
    }
}
