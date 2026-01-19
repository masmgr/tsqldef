using System.Linq;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class CurrentSchemaReaderTests
{
    [Fact]
    public void BuildModel_MapsTablesAndColumns()
    {
        var tables = new[]
        {
            new CurrentSchemaReader.TableRow
            {
                SchemaName = "dbo",
                TableName = "Users",
                ObjectId = 1,
            },
            new CurrentSchemaReader.TableRow
            {
                SchemaName = "dbo",
                TableName = "Teams",
                ObjectId = 2,
            },
        };

        var columns = new[]
        {
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1,
                ColumnId = 1,
                ColumnName = "Id",
                IsNullable = false,
                TypeName = "int",
                MaxLength = 0,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = true,
            },
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1,
                ColumnId = 2,
                ColumnName = "Name",
                IsNullable = true,
                TypeName = "nvarchar",
                MaxLength = 200,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = false,
            },
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 2,
                ColumnId = 1,
                ColumnName = "TeamId",
                IsNullable = false,
                TypeName = "uniqueidentifier",
                MaxLength = 0,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = false,
            },
        };

        var defaults = new[]
        {
            new CurrentSchemaReader.DefaultRow
            {
                ObjectId = 1,
                ColumnId = 2,
                DefaultDefinition = "('unknown')",
            },
        };

        var model = CurrentSchemaReader.BuildModel(tables, columns, defaults);

        Assert.Equal(2, model.Tables.Count);

        var users = model.Tables.Values.Single(table => table.Name == "Users");
        Assert.True(users.Columns.ContainsKey("ID"));
        Assert.True(users.Columns.ContainsKey("NAME"));

        var id = users.Columns["ID"];
        Assert.Equal("int", id.SqlType);
        Assert.False(id.IsNullable);
        Assert.True(id.IsIdentity);

        var name = users.Columns["NAME"];
        Assert.Equal("nvarchar(100)", name.SqlType);
        Assert.True(name.IsNullable);
        Assert.False(name.IsIdentity);
        Assert.Equal("('unknown')", name.DefaultExpression);

        var teams = model.Tables.Values.Single(table => table.Name == "Teams");
        var teamId = teams.Columns["TEAMID"];
        Assert.Equal("uniqueidentifier", teamId.SqlType);
    }
}
