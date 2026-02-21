using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class CurrentSchemaRowGroupingTests
{
    [Fact]
    public void BuildDefaultMap_NullInput_ReturnsEmpty()
    {
        var map = CurrentSchemaRowGrouping.BuildDefaultMap(null);
        Assert.Empty(map);
    }

    [Fact]
    public void BuildDefaultMap_NullRowsAreIgnored()
    {
        var rows = new CurrentSchemaReader.DefaultRow[]
        {
            null!,
            new CurrentSchemaReader.DefaultRow { ObjectId = 1, ColumnId = 2, DefaultDefinition = "(0)" },
        };

        var map = CurrentSchemaRowGrouping.BuildDefaultMap(rows);

        Assert.Single(map);
        Assert.Equal("(0)", map[(1, 2)]);
    }

    [Fact]
    public void BuildKeyConstraintGroups_NullNamesAreNormalizedToEmptyKey()
    {
        var rows = new[]
        {
            new CurrentSchemaReader.KeyConstraintRow { ObjectId = 10, ConstraintName = null, ColumnName = "Id" },
            new CurrentSchemaReader.KeyConstraintRow { ObjectId = 10, ConstraintName = string.Empty, ColumnName = "Name" },
        };

        var groups = CurrentSchemaRowGrouping.BuildKeyConstraintGroups(rows);

        Assert.Single(groups);
        Assert.Equal(2, groups[(10, string.Empty)].Count);
    }

    [Fact]
    public void BuildForeignKeyGroups_NullRowsAreIgnored()
    {
        var rows = new CurrentSchemaReader.ForeignKeyRow[]
        {
            null!,
            new CurrentSchemaReader.ForeignKeyRow { ParentObjectId = 1, ConstraintName = "FK_A" },
        };

        var groups = CurrentSchemaRowGrouping.BuildForeignKeyGroups(rows);

        Assert.Single(groups);
        Assert.Single(groups[(1, "FK_A")]);
    }

    [Fact]
    public void BuildIndexGroups_NullNamesAreNormalizedToEmptyKey()
    {
        var rows = new[]
        {
            new CurrentSchemaReader.IndexRow { ObjectId = 3, IndexName = null, ColumnName = "C1" },
            new CurrentSchemaReader.IndexRow { ObjectId = 3, IndexName = string.Empty, ColumnName = "C2" },
        };

        var groups = CurrentSchemaRowGrouping.BuildIndexGroups(rows);

        Assert.Single(groups);
        Assert.Equal(2, groups[(3, string.Empty)].Count);
    }
}
