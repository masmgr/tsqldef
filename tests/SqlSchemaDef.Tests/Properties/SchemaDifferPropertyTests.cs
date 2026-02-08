using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using SqlSchemaDef.Tests.Properties.Generators;

namespace SqlSchemaDef.Tests.Properties;

public sealed class SchemaDifferPropertyTests
{
    [Property(MaxTest = 100)]
    public Property Diff_SameModel_ProducesZeroOperations()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenDatabaseModel()), model =>
        {
            var metadata = new PlanMetadata { Schema = "dbo" };
            var plan = SchemaDiffer.Diff(model, model, metadata);
            return (plan.Operations.Count == 0)
                .Label($"Expected 0 operations, got {plan.Operations.Count}");
        });
    }

    [Property(MaxTest = 100)]
    public Property Diff_IsDeterministic_RegardlessOfInsertionOrder()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenDatabaseModel()), model =>
        {
            var reversed = ReverseInsertionOrder(model);
            var empty = new DatabaseModel();
            var metadata = new PlanMetadata { Schema = "dbo" };

            var plan1 = SchemaDiffer.Diff(empty, model, metadata);
            var plan2 = SchemaDiffer.Diff(new DatabaseModel(), reversed, metadata);

            var sql1 = plan1.Operations.Select(op => op.Sql).ToArray();
            var sql2 = plan2.Operations.Select(op => op.Sql).ToArray();

            return sql1.SequenceEqual(sql2)
                .Label($"plan1=[{string.Join("; ", sql1)}] plan2=[{string.Join("; ", sql2)}]");
        });
    }

    [Property(MaxTest = 100)]
    public Property DatabaseModel_TableLookup_IsCaseInsensitive()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), name =>
        {
            var model = new DatabaseModel();
            model.GetOrAddTable("dbo", name);

            var upperKey = IdentifierHelper.BuildTableKey("dbo", name.ToUpperInvariant());
            var lowerKey = IdentifierHelper.BuildTableKey("dbo", name.ToLowerInvariant());

            return (model.Tables.ContainsKey(upperKey) && model.Tables.ContainsKey(lowerKey))
                .Label($"name='{name}' upper='{upperKey}' lower='{lowerKey}'");
        });
    }

    [Property(MaxTest = 100)]
    public Property Diff_Operations_AreInCorrectOrder()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenDatabaseModel()), desired =>
        {
            var empty = new DatabaseModel();
            var metadata = new PlanMetadata { Schema = "dbo" };
            var plan = SchemaDiffer.Diff(empty, desired, metadata);

            var kinds = plan.Operations.Select(op => op.Kind).ToArray();
            for (int i = 1; i < kinds.Length; i++)
            {
                if (kinds[i] < kinds[i - 1])
                {
                    return false.Label(
                        $"Order violation at index {i}: {kinds[i - 1]} followed by {kinds[i]}");
                }
            }

            return true.ToProperty();
        });
    }

    private static DatabaseModel ReverseInsertionOrder(DatabaseModel original)
    {
        var reversed = new DatabaseModel();
        var tables = original.Tables.Reverse().ToArray();
        foreach (var entry in tables)
        {
            var origTable = entry.Value;
            var newTable = new TableModel(origTable.Schema, origTable.Name);

            foreach (var col in origTable.Columns.Reverse())
            {
                newTable.Columns[col.Key] = col.Value;
            }

            foreach (var constraint in origTable.Constraints.Reverse())
            {
                newTable.Constraints[constraint.Key] = constraint.Value;
            }

            foreach (var index in origTable.Indexes.Reverse())
            {
                newTable.Indexes[index.Key] = index.Value;
            }

            reversed.Tables[entry.Key] = newTable;
        }

        return reversed;
    }
}
