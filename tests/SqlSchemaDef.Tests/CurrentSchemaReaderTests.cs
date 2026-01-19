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

        var keyConstraints = new[]
        {
            new CurrentSchemaReader.KeyConstraintRow
            {
                ObjectId = 1,
                ConstraintName = "PK_Users",
                ConstraintType = "PK",
                KeyOrdinal = 1,
                ColumnName = "Id",
            },
            new CurrentSchemaReader.KeyConstraintRow
            {
                ObjectId = 1,
                ConstraintName = "UQ_Users_Name",
                ConstraintType = "UQ",
                KeyOrdinal = 1,
                ColumnName = "Name",
            },
        };

        var checkConstraints = new[]
        {
            new CurrentSchemaReader.CheckConstraintRow
            {
                ObjectId = 1,
                ConstraintName = "CK_Users_Name",
                Definition = "([Name] <> '')",
            },
        };

        var model = CurrentSchemaReader.BuildModel(tables, columns, defaults, keyConstraints, checkConstraints);

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

        Assert.True(users.Constraints.ContainsKey("PK_USERS"));
        Assert.True(users.Constraints.ContainsKey("UQ_USERS_NAME"));

        var pk = users.Constraints["PK_USERS"];
        Assert.Equal(ConstraintKind.PrimaryKey, pk.Kind);
        Assert.Equal("Id", pk.Columns.Single());

        var uq = users.Constraints["UQ_USERS_NAME"];
        Assert.Equal(ConstraintKind.Unique, uq.Kind);
        Assert.Equal("Name", uq.Columns.Single());

        var check = users.Constraints["CK_USERS_NAME"];
        Assert.Equal(ConstraintKind.Check, check.Kind);
        Assert.Equal("([Name] <> '')", check.Definition);

        var teams = model.Tables.Values.Single(table => table.Name == "Teams");
        var teamId = teams.Columns["TEAMID"];
        Assert.Equal("uniqueidentifier", teamId.SqlType);
    }
}
