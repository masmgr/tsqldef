using System.Linq;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class DesiredSqlParserTests
{
    [Fact]
    public void ParseBatches_ReportsAdjustedDiagnostics()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.A (Id int)",
            "GO",
            "CREATE TABLE dbo.B (Id int",
        });

        var batches = BatchSplitter.Split(sql);
        var parser = new DesiredSqlParser();

        var ex = Assert.Throws<DesiredSqlParseException>(() => parser.ParseBatches(batches));

        Assert.NotEmpty(ex.Diagnostics);
        var diagnostic = ex.Diagnostics[0];
        Assert.Equal(1, diagnostic.BatchIndex);
        Assert.Equal(3, diagnostic.Line);
        Assert.True(diagnostic.Column.HasValue);
        Assert.Contains("Failed to parse desired SQL.", ex.Message);
        Assert.Contains("Batch 1, line 3", ex.Message);
    }

    [Fact]
    public void ParseBatches_CollectsDiagnosticsAcrossBatches()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.A (Id int",
            "GO",
            "CREATE TABLE dbo.B (Id int",
        });

        var batches = BatchSplitter.Split(sql);
        var parser = new DesiredSqlParser();

        var ex = Assert.Throws<DesiredSqlParseException>(() => parser.ParseBatches(batches));

        Assert.True(ex.Diagnostics.Count >= 2);
        Assert.Contains(ex.Diagnostics, diag => diag.BatchIndex == 0);
        Assert.Contains(ex.Diagnostics, diag => diag.BatchIndex == 1);
    }
}
