using System;
using System.Collections.Generic;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class CurrentSchemaModelBuilderHelpersTests
{
    [Fact]
    public void GetDefaultDefinition_WhenMissing_ReturnsNull()
    {
        var map = new Dictionary<(int ObjectId, int ColumnId), string>();
        var result = CurrentSchemaModelBuilderHelpers.GetDefaultDefinition(map, 1, 1);

        Assert.Null(result);
    }

    [Fact]
    public void GetDefaultDefinition_WhenPresent_ReturnsValue()
    {
        var map = new Dictionary<(int ObjectId, int ColumnId), string>
        {
            [(1, 2)] = "(42)",
        };

        var result = CurrentSchemaModelBuilderHelpers.GetDefaultDefinition(map, 1, 2);

        Assert.Equal("(42)", result);
    }

    [Fact]
    public void ResolveKeyConstraintKind_MapsPkAndUq()
    {
        Assert.Equal(ConstraintKind.PrimaryKey, CurrentSchemaModelBuilderHelpers.ResolveKeyConstraintKind("PK"));
        Assert.Equal(ConstraintKind.Unique, CurrentSchemaModelBuilderHelpers.ResolveKeyConstraintKind("uq"));
    }

    [Fact]
    public void ResolveKeyConstraintKind_Unsupported_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CurrentSchemaModelBuilderHelpers.ResolveKeyConstraintKind("CK"));

        Assert.Contains("Unsupported key constraint type", ex.Message);
    }

    [Fact]
    public void ResolveReferenceSchema_WhitespaceDefaultsToDbo()
    {
        Assert.Equal("dbo", CurrentSchemaModelBuilderHelpers.ResolveReferenceSchema(null));
        Assert.Equal("dbo", CurrentSchemaModelBuilderHelpers.ResolveReferenceSchema("   "));
        Assert.Equal("sales", CurrentSchemaModelBuilderHelpers.ResolveReferenceSchema("sales"));
    }

    [Fact]
    public void BuildIndexOptionsFromRow_WhenNoOptions_ReturnsNull()
    {
        var row = new CurrentSchemaReader.IndexRow
        {
            FillFactor = 0,
            IsPadded = false,
            IgnoreDupKey = false,
            AllowRowLocks = true,
            AllowPageLocks = true,
            NoRecompute = false,
        };

        var options = CurrentSchemaModelBuilderHelpers.BuildIndexOptionsFromRow(row);

        Assert.Null(options);
    }

    [Fact]
    public void BuildIndexOptionsFromRow_WhenOptionsExist_MapsExpectedKeys()
    {
        var row = new CurrentSchemaReader.IndexRow
        {
            FillFactor = 90,
            IsPadded = true,
            IgnoreDupKey = true,
            AllowRowLocks = false,
            AllowPageLocks = false,
            NoRecompute = true,
        };

        var options = CurrentSchemaModelBuilderHelpers.BuildIndexOptionsFromRow(row);

        Assert.NotNull(options);
        Assert.Equal("90", options["FILLFACTOR"]);
        Assert.Equal("ON", options["PAD_INDEX"]);
        Assert.Equal("ON", options["IGNORE_DUP_KEY"]);
        Assert.Equal("OFF", options["ALLOW_ROW_LOCKS"]);
        Assert.Equal("OFF", options["ALLOW_PAGE_LOCKS"]);
        Assert.Equal("ON", options["STATISTICS_NORECOMPUTE"]);
    }
}
