using System;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using SqlSchemaDef.Tests.Properties.Generators;

namespace SqlSchemaDef.Tests.Properties;

public sealed class BatchSplitterPropertyTests
{
    [Property(MaxTest = 100)]
    public Property Split_NoGo_ReturnsSingleBatch()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), input =>
        {
            // SQL identifiers won't contain "GO" as a standalone line
            var sql = "SELECT " + input;
            var batches = BatchSplitter.Split(sql);
            return (batches.Count <= 1)
                .Label($"input='{sql}' count={batches.Count}");
        });
    }

    [Property(MaxTest = 100)]
    public Property Split_AllBatchTexts_AreNonWhitespace()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), input =>
        {
            var sql = "SELECT " + input + "\nGO\nSELECT 1";
            var batches = BatchSplitter.Split(sql);
            return batches.All(b => !string.IsNullOrWhiteSpace(b.Text))
                .Label("Some batch text was whitespace");
        });
    }

    [Property(MaxTest = 100)]
    public Property Split_BatchIndices_AreSequential()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), input =>
        {
            var sql = "SELECT " + input + "\nGO\nSELECT 1\nGO\nSELECT 2";
            var batches = BatchSplitter.Split(sql);
            for (int i = 0; i < batches.Count; i++)
            {
                if (batches[i].BatchIndex != i)
                    return false.Label($"Expected index {i}, got {batches[i].BatchIndex}");
            }

            return true.ToProperty();
        });
    }

    [Property(MaxTest = 100)]
    public Property Split_GoInBlockComment_DoesNotSplit()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), input =>
        {
            var sql = "SELECT " + input + "\n/* GO */\nSELECT 1";
            var batches = BatchSplitter.Split(sql);
            return (batches.Count == 1)
                .Label($"Expected 1 batch, got {batches.Count}");
        });
    }
}
