using System;
using System.Collections.Generic;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class SchemaExportScriptBuilderTests
{
    [Fact]
    public void BuildScript_WhenTableHasOnlyUnsupportedColumns_AddsSkippedColumnAndTable()
    {
        var model = new DatabaseModel();
        var table = model.GetOrAddTable("dbo", "Users");
        table.Columns["AGE"] = new ColumnModel
        {
            Name = "Age",
            SqlType = "int",
            IsNullable = true,
            UnsupportedFeature = "ComputedColumn",
        };

        var skipped = new List<SkippedItem>();
        var script = SchemaExportScriptBuilder.BuildScript(
            model,
            new ExportOptions { IncludeSkipped = true, NewLine = "\n" },
            skipped);

        Assert.Equal(2, skipped.Count);
        Assert.Contains(skipped, item => item.Target?.ToDisplayName() == "dbo.Users.Age");
        Assert.Contains(skipped, item => item.Target?.ToDisplayName() == "dbo.Users");
        Assert.Contains("-- Skipped: UnsupportedFeatureInCurrent dbo.Users.Age - unsupported column feature: ComputedColumn", script);
        Assert.Contains("-- Skipped: UnsupportedFeatureInCurrent dbo.Users - no exportable columns", script);
    }

    [Fact]
    public void BuildScript_WhenIndexesAreUnsupported_AddsSkippedAndOmitsCreateIndexSql()
    {
        var model = new DatabaseModel();
        var table = model.GetOrAddTable("dbo", "Users");
        table.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Indexes["IX_USERS_UNSUPPORTED"] = new IndexModel
        {
            Name = "IX_Users_Unsupported",
            UnsupportedFeature = "FilteredIndex",
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
        };
        table.Indexes["IX_USERS_NOKEY"] = new IndexModel
        {
            Name = "IX_Users_NoKey",
            KeyColumns = new List<IndexKeyColumn>(),
        };

        var skipped = new List<SkippedItem>();
        var script = SchemaExportScriptBuilder.BuildScript(
            model,
            new ExportOptions { IncludeSkipped = true, NewLine = "\n" },
            skipped);

        Assert.Equal(2, skipped.Count);
        Assert.DoesNotContain("CREATE NONCLUSTERED INDEX", script);
        Assert.Contains("unsupported index feature: FilteredIndex", script);
        Assert.Contains("index has no key columns", script);
    }

    [Fact]
    public void BuildScript_WhenDescriptionsExist_EmitsDescriptionStatements()
    {
        var model = new DatabaseModel();
        var table = model.GetOrAddTable("dbo", "Users");
        table.Description = "User accounts";
        table.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false, Description = "Primary key" };

        var skipped = new List<SkippedItem>();
        var script = SchemaExportScriptBuilder.BuildScript(
            model,
            new ExportOptions { IncludeSkipped = true, NewLine = "\r\n" },
            skipped);

        Assert.Contains("EXEC sp_addextendedproperty", script);
        Assert.Contains("@level1name = N'Users'", script);
        Assert.Contains("@level2name = N'Id'", script);
        Assert.Contains("\r\n\r\nEXEC sp_addextendedproperty", script);
    }

    [Fact]
    public void BuildScript_WhenIncludeSkippedIsFalse_DoesNotAppendSkippedComments()
    {
        var model = new DatabaseModel();
        var table = model.GetOrAddTable("dbo", "Users");
        table.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        table.Indexes["IX_USERS_NOKEY"] = new IndexModel
        {
            Name = "IX_Users_NoKey",
            KeyColumns = new List<IndexKeyColumn>(),
        };

        var skipped = new List<SkippedItem>();
        var script = SchemaExportScriptBuilder.BuildScript(
            model,
            new ExportOptions { IncludeSkipped = false, NewLine = "\n" },
            skipped);

        Assert.NotEmpty(skipped);
        Assert.DoesNotContain("-- Skipped:", script);
    }

    [Fact]
    public void BuildScript_OrdersTablesAndIndexesDeterministically()
    {
        var model = new DatabaseModel();

        var beta = model.GetOrAddTable("dbo", "Beta");
        beta.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        beta.Indexes["IX_BETA_Z"] = new IndexModel
        {
            Name = "IX_Beta_Z",
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
        };

        var alpha = model.GetOrAddTable("dbo", "Alpha");
        alpha.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "INT", IsNullable = false };
        alpha.Indexes["IX_ALPHA_A"] = new IndexModel
        {
            Name = "IX_Alpha_A",
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = "Id" } },
        };

        var script = SchemaExportScriptBuilder.BuildScript(
            model,
            new ExportOptions { IncludeSkipped = true, NewLine = "\n" },
            new List<SkippedItem>());

        var alphaTablePos = script.IndexOf("CREATE TABLE [dbo].[Alpha]", StringComparison.Ordinal);
        var betaTablePos = script.IndexOf("CREATE TABLE [dbo].[Beta]", StringComparison.Ordinal);
        var alphaIndexPos = script.IndexOf("CREATE NONCLUSTERED INDEX [IX_Alpha_A]", StringComparison.Ordinal);
        var betaIndexPos = script.IndexOf("CREATE NONCLUSTERED INDEX [IX_Beta_Z]", StringComparison.Ordinal);

        Assert.True(alphaTablePos >= 0);
        Assert.True(betaTablePos > alphaTablePos);
        Assert.True(alphaIndexPos > betaTablePos);
        Assert.True(betaIndexPos > alphaIndexPos);
    }
}
