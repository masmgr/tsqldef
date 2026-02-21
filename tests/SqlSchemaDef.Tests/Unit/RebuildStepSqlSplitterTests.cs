using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class RebuildStepSqlSplitterTests
{
    [Fact]
    public void Split_Null_ReturnsEmpty()
    {
        var result = RebuildStepSqlSplitter.Split(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Split_EmptyString_ReturnsEmpty()
    {
        var result = RebuildStepSqlSplitter.Split(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Split_SingleStatement_ReturnsSingleItem()
    {
        var sql = "CREATE TABLE [dbo].[__Users_rebuild] ([Id] INT NOT NULL)";
        var result = RebuildStepSqlSplitter.Split(sql);
        Assert.Single(result);
        Assert.Equal(sql, result[0]);
    }

    [Fact]
    public void Split_TwoStatements_SplitsOnSemicolonNewline()
    {
        var sql = "ALTER TABLE [dbo].[T] DROP CONSTRAINT [PK_T];\nALTER TABLE [dbo].[T] DROP CONSTRAINT [UQ_T]";
        var result = RebuildStepSqlSplitter.Split(sql);
        Assert.Equal(2, result.Count);
        Assert.Equal("ALTER TABLE [dbo].[T] DROP CONSTRAINT [PK_T]", result[0]);
        Assert.Equal("ALTER TABLE [dbo].[T] DROP CONSTRAINT [UQ_T]", result[1]);
    }

    [Fact]
    public void Split_IdentityInsertWrapper_SplitsIntoThree()
    {
        var sql =
            "SET IDENTITY_INSERT [dbo].[__Users_rebuild] ON;\n" +
            "INSERT INTO [dbo].[__Users_rebuild] ([Id]) SELECT [Id] FROM [dbo].[Users];\n" +
            "SET IDENTITY_INSERT [dbo].[__Users_rebuild] OFF";

        var result = RebuildStepSqlSplitter.Split(sql);
        Assert.Equal(3, result.Count);
        Assert.StartsWith("SET IDENTITY_INSERT", result[0]);
        Assert.Contains("ON", result[0]);
        Assert.StartsWith("INSERT INTO", result[1]);
        Assert.StartsWith("SET IDENTITY_INSERT", result[2]);
        Assert.Contains("OFF", result[2]);
    }

    [Fact]
    public void Split_WindowsLineEnding_SplitsCorrectly()
    {
        var sql = "ALTER TABLE [dbo].[T] DROP CONSTRAINT [PK_T];\r\nALTER TABLE [dbo].[T] DROP CONSTRAINT [UQ_T]";
        var result = RebuildStepSqlSplitter.Split(sql);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Split_CommentOnlyStatement_ReturnsSingleItem()
    {
        var sql = "-- No common columns to copy";
        var result = RebuildStepSqlSplitter.Split(sql);
        Assert.Single(result);
        Assert.Equal(sql, result[0]);
    }

    [Fact]
    public void Split_TrimsWhitespace()
    {
        var sql = "  CREATE TABLE [dbo].[X] ([Id] INT NOT NULL)  ";
        var result = RebuildStepSqlSplitter.Split(sql);
        Assert.Single(result);
        Assert.Equal("CREATE TABLE [dbo].[X] ([Id] INT NOT NULL)", result[0]);
    }
}
