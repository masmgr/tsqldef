using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.Tests;

public sealed class SqlObjectRefTests
{
    [Fact]
    public void ToDisplayName_DescriptionWithParentName_UsesTableAndColumnShape()
    {
        var target = new SqlObjectRef
        {
            Type = SqlObjectType.Description,
            Schema = "dbo",
            ParentName = "Users",
            Name = "Id",
        };

        Assert.Equal("dbo.Users.Id", target.ToDisplayName());
    }

    [Fact]
    public void ToDisplayName_DescriptionWithoutParentName_UsesSchemaAndName()
    {
        var target = new SqlObjectRef
        {
            Type = SqlObjectType.Description,
            Schema = "dbo",
            Name = "Users",
        };

        Assert.Equal("dbo.Users", target.ToDisplayName());
    }

    [Fact]
    public void ToDisplayName_UnknownType_FallsBackToSchemaAndName()
    {
        var target = new SqlObjectRef
        {
            Type = (SqlObjectType)999,
            Schema = "dbo",
            Name = "X",
        };

        Assert.Equal("dbo.X", target.ToDisplayName());
    }
}
