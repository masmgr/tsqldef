using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class BatchSplitterTests
{
    [Fact]
    public void Split_BasicGoSeparators_AreCaseInsensitive()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.A (Id int)",
            "GO",
            "CREATE TABLE dbo.B (Id int)",
            "gO",
        });

        var batches = BatchSplitter.Split(sql);

        Assert.Equal(2, batches.Count);
        Assert.Equal(0, batches[0].BatchIndex);
        Assert.Equal(1, batches[0].StartLine);
        Assert.Equal("CREATE TABLE dbo.A (Id int)", batches[0].Text);
        Assert.Equal(1, batches[1].BatchIndex);
        Assert.Equal(3, batches[1].StartLine);
        Assert.Equal("CREATE TABLE dbo.B (Id int)", batches[1].Text);
    }

    [Fact]
    public void Split_IgnoresGoInsideComments()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.A (Id int)",
            "-- GO",
            "/* GO */",
            "GO",
            "CREATE TABLE dbo.B (Id int)",
        });

        var batches = BatchSplitter.Split(sql);

        Assert.Equal(2, batches.Count);
        Assert.Contains("-- GO", batches[0].Text);
        Assert.Contains("/* GO */", batches[0].Text);
        Assert.Equal(1, batches[0].StartLine);
        Assert.Equal(5, batches[1].StartLine);
    }

    [Fact]
    public void Split_IgnoresTrailingEmptyBatch()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.A (Id int)",
            "GO",
        });

        var batches = BatchSplitter.Split(sql);

        Assert.Single(batches);
        Assert.Equal("CREATE TABLE dbo.A (Id int)", batches[0].Text);
    }

    [Fact]
    public void Split_GoWithRepeatCount_Throws()
    {
        var sql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.A (Id int)",
            "GO 2",
            "CREATE TABLE dbo.B (Id int)",
        });

        var ex = Assert.Throws<UnsupportedBatchSeparatorException>(() => BatchSplitter.Split(sql));

        Assert.Contains("GO 2", ex.Message);
        Assert.Equal(2, ex.Line);
    }
}
