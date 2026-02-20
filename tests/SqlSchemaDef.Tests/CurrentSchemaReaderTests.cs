using System;
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

        var foreignKeys = new[]
        {
            new CurrentSchemaReader.ForeignKeyRow
            {
                ParentObjectId = 1,
                ConstraintName = "FK_Users_Teams",
                ReferencedSchemaName = "dbo",
                ReferencedTableName = "Teams",
                Ordinal = 1,
                ParentColumnName = "TeamId",
                ReferencedColumnName = "TeamId",
            },
        };

        var indexes = new[]
        {
            new CurrentSchemaReader.IndexRow
            {
                ObjectId = 1,
                IndexName = "IX_Users_Name",
                IsUnique = false,
                KeyOrdinal = 1,
                IsIncludedColumn = false,
                IsDescendingKey = false,
                ColumnName = "Name",
            },
        };

        var model = CurrentSchemaReader.BuildModel(
            tables,
            columns,
            defaults,
            keyConstraints,
            checkConstraints,
            foreignKeys,
            indexes);

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

        var fk = users.Constraints["FK_USERS_TEAMS"];
        Assert.Equal(ConstraintKind.ForeignKey, fk.Kind);
        Assert.Equal("dbo", fk.ReferenceSchema);
        Assert.Equal("Teams", fk.ReferenceTable);
        Assert.Equal("TeamId", fk.Columns.Single());
        Assert.Equal("TeamId", fk.ReferenceColumns.Single());

        var ix = users.Indexes["IX_USERS_NAME"];
        Assert.False(ix.IsUnique);
        Assert.Equal("Name", ix.KeyColumns.Single().Name);
        Assert.False(ix.KeyColumns.Single().IsDescending);

        var teams = model.Tables.Values.Single(table => table.Name == "Teams");
        var teamId = teams.Columns["TEAMID"];
        Assert.Equal("uniqueidentifier", teamId.SqlType);
    }

    [Fact]
    public void BuildModel_CompositeKeysAndForeignKeys_PreserveOrdinalOrder()
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
                ColumnName = "UserId",
                IsNullable = false,
                TypeName = "int",
                MaxLength = 0,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = false,
            },
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1,
                ColumnId = 2,
                ColumnName = "TeamId",
                IsNullable = false,
                TypeName = "int",
                MaxLength = 0,
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
                TypeName = "int",
                MaxLength = 0,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = false,
            },
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 2,
                ColumnId = 2,
                ColumnName = "UserId",
                IsNullable = false,
                TypeName = "int",
                MaxLength = 0,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = false,
            },
        };

        var keyConstraints = new[]
        {
            new CurrentSchemaReader.KeyConstraintRow
            {
                ObjectId = 1,
                ConstraintName = "PK_Users",
                ConstraintType = "PK",
                KeyOrdinal = 2,
                ColumnName = "TeamId",
            },
            new CurrentSchemaReader.KeyConstraintRow
            {
                ObjectId = 1,
                ConstraintName = "PK_Users",
                ConstraintType = "PK",
                KeyOrdinal = 1,
                ColumnName = "UserId",
            },
        };

        var foreignKeys = new[]
        {
            new CurrentSchemaReader.ForeignKeyRow
            {
                ParentObjectId = 1,
                ConstraintName = "FK_Users_Teams",
                ReferencedSchemaName = "dbo",
                ReferencedTableName = "Teams",
                Ordinal = 2,
                ParentColumnName = "TeamId",
                ReferencedColumnName = "TeamId",
            },
            new CurrentSchemaReader.ForeignKeyRow
            {
                ParentObjectId = 1,
                ConstraintName = "FK_Users_Teams",
                ReferencedSchemaName = "dbo",
                ReferencedTableName = "Teams",
                Ordinal = 1,
                ParentColumnName = "UserId",
                ReferencedColumnName = "UserId",
            },
        };

        var model = CurrentSchemaReader.BuildModel(
            tables,
            columns,
            keyConstraints: keyConstraints,
            foreignKeys: foreignKeys);

        var users = model.Tables.Values.Single(table => table.Name == "Users");
        var pk = users.Constraints["PK_USERS"];
        Assert.Equal(new[] { "UserId", "TeamId" }, pk.Columns);

        var fk = users.Constraints["FK_USERS_TEAMS"];
        Assert.Equal(new[] { "UserId", "TeamId" }, fk.Columns);
        Assert.Equal(new[] { "UserId", "TeamId" }, fk.ReferenceColumns);
    }

    [Fact]
    public void BuildModel_IndexIncludeAndSortOrder_AreCaptured()
    {
        var tables = new[]
        {
            new CurrentSchemaReader.TableRow
            {
                SchemaName = "dbo",
                TableName = "Users",
                ObjectId = 1,
            },
        };

        var columns = new[]
        {
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1,
                ColumnId = 1,
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
                ObjectId = 1,
                ColumnId = 2,
                ColumnName = "Age",
                IsNullable = true,
                TypeName = "int",
                MaxLength = 0,
                Precision = 0,
                Scale = 0,
                IsComputed = false,
                IsIdentity = false,
            },
        };

        var indexes = new[]
        {
            new CurrentSchemaReader.IndexRow
            {
                ObjectId = 1,
                IndexName = "IX_Users_Name",
                IsUnique = false,
                KeyOrdinal = 1,
                IsIncludedColumn = false,
                IsDescendingKey = true,
                ColumnName = "Name",
            },
            new CurrentSchemaReader.IndexRow
            {
                ObjectId = 1,
                IndexName = "IX_Users_Name",
                IsUnique = false,
                KeyOrdinal = 0,
                IsIncludedColumn = true,
                IsDescendingKey = false,
                ColumnName = "Age",
            },
        };

        var model = CurrentSchemaReader.BuildModel(tables, columns, indexes: indexes);
        var table = model.Tables.Values.Single();
        var index = table.Indexes.Values.Single();

        Assert.Equal("IX_Users_Name", index.Name);
        Assert.Null(index.UnsupportedFeature);
        Assert.Single(index.KeyColumns);
        Assert.Equal("Name", index.KeyColumns[0].Name);
        Assert.True(index.KeyColumns[0].IsDescending);
        Assert.Equal("Age", Assert.Single(index.IncludeColumns));
    }

    [Fact]
    public void BuildModel_WithExtendedProperties_SetsTableDescription()
    {
        var tables = new[]
        {
            new CurrentSchemaReader.TableRow { SchemaName = "dbo", TableName = "Users", ObjectId = 1 },
        };

        var columns = new[]
        {
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1, ColumnId = 1, ColumnName = "Id",
                IsNullable = false, TypeName = "int", IsComputed = false, IsIdentity = false,
            },
        };

        var extendedProperties = new[]
        {
            new CurrentSchemaReader.ExtendedPropertyRow { MajorId = 1, MinorId = 0, PropertyValue = "User accounts" },
        };

        var model = CurrentSchemaReader.BuildModel(tables, columns, extendedProperties: extendedProperties);
        var table = model.Tables.Values.Single();

        Assert.Equal("User accounts", table.Description);
    }

    [Fact]
    public void BuildModel_WithExtendedProperties_SetsColumnDescription()
    {
        var tables = new[]
        {
            new CurrentSchemaReader.TableRow { SchemaName = "dbo", TableName = "Users", ObjectId = 1 },
        };

        var columns = new[]
        {
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1, ColumnId = 1, ColumnName = "Id",
                IsNullable = false, TypeName = "int", IsComputed = false, IsIdentity = false,
            },
            new CurrentSchemaReader.ColumnRow
            {
                ObjectId = 1, ColumnId = 2, ColumnName = "Name",
                IsNullable = true, TypeName = "nvarchar", MaxLength = 200, IsComputed = false, IsIdentity = false,
            },
        };

        var extendedProperties = new[]
        {
            new CurrentSchemaReader.ExtendedPropertyRow { MajorId = 1, MinorId = 2, PropertyValue = "User name" },
        };

        var model = CurrentSchemaReader.BuildModel(tables, columns, extendedProperties: extendedProperties);
        var table = model.Tables.Values.Single();

        Assert.Null(table.Columns["ID"].Description);
        Assert.Equal("User name", table.Columns["NAME"].Description);
    }
}
