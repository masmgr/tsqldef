using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests.Properties.Generators;

public static class DomainArbitraries
{
    private static readonly char[] AlphaChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    public static readonly string[] SqlKeywords =
    {
        "SELECT", "FROM", "WHERE", "TABLE", "INDEX", "CREATE", "ALTER", "DROP",
        "INSERT", "UPDATE", "DELETE", "INTO", "VALUES", "SET", "ORDER", "BY",
        "GROUP", "HAVING", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON",
        "AND", "OR", "NOT", "NULL", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
        "CONSTRAINT", "UNIQUE", "CHECK", "DEFAULT", "IDENTITY", "GO",
        "BEGIN", "END", "IF", "ELSE", "WHILE", "RETURN", "EXEC", "EXECUTE",
        "GRANT", "REVOKE", "DENY", "USER", "VIEW", "PROCEDURE", "FUNCTION",
    };

    public static readonly string[] SimpleTypes =
    {
        "int", "bigint", "bit", "date", "datetime", "float", "real",
        "smallint", "tinyint", "uniqueidentifier", "money", "smallmoney",
    };

    private static readonly string[] LengthTypes = { "varchar", "char", "varbinary", "binary" };
    private static readonly string[] NLengthTypes = { "nvarchar", "nchar" };
    private static readonly string[] PrecisionTypes = { "decimal", "numeric" };
    private static readonly string[] ScaleTypes = { "datetime2", "datetimeoffset", "time" };

    public static Gen<string> GenSqlIdentifier() =>
        from len in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(len, Gen.Elements(AlphaChars))
        select new string(chars);

    public static Gen<string> GenSqlKeyword() =>
        Gen.Elements(SqlKeywords);

    public static Gen<(string TypeName, int MaxLength, byte Precision, byte Scale)> GenSqlTypeFormatterInput()
    {
        var simpleGen =
            from t in Gen.Elements(SimpleTypes)
            select (t, 0, (byte)0, (byte)0);

        var lengthGen =
            from t in Gen.Elements(LengthTypes)
            from len in Gen.OneOf(Gen.Constant(-1), Gen.Choose(1, 8000))
            select (t, len, (byte)0, (byte)0);

        var nLengthGen =
            from t in Gen.Elements(NLengthTypes)
            from len in Gen.OneOf(Gen.Constant(-1), Gen.Choose(1, 4000).Select(i => i * 2))
            select (t, len, (byte)0, (byte)0);

        var precisionGen =
            from t in Gen.Elements(PrecisionTypes)
            from p in Gen.Choose(1, 38).Select(i => (byte)i)
            from s in Gen.Choose(0, (int)p).Select(i => (byte)i)
            select (t, 0, p, s);

        var scaleGen =
            from t in Gen.Elements(ScaleTypes)
            from s in Gen.Choose(0, 7).Select(i => (byte)i)
            select (t, 0, (byte)0, s);

        return Gen.OneOf(simpleGen, lengthGen, nLengthGen, precisionGen, scaleGen);
    }

    public static Arbitrary<(string TypeName, int MaxLength, byte Precision, byte Scale)> SqlTypeFormatterInputArb() =>
        Arb.From(GenSqlTypeFormatterInput());

    public static Gen<ColumnModel> GenColumnModel() =>
        from name in GenSqlIdentifier()
        from sqlType in Gen.Elements(SimpleTypes)
        from isNullable in Arb.Generate<bool>()
        from isIdentity in Arb.Generate<bool>()
        select new ColumnModel
        {
            Name = name,
            SqlType = sqlType,
            IsNullable = isNullable,
            IsIdentity = isIdentity,
        };

    public static Gen<ConstraintModel> GenConstraintModel(IReadOnlyList<string> columnNames) =>
        from kind in Gen.Elements(ConstraintKind.PrimaryKey, ConstraintKind.Unique, ConstraintKind.Check)
        from name in GenSqlIdentifier()
        from isClustered in Arb.Generate<bool>()
        select BuildConstraint(kind, name, columnNames, isClustered);

    public static Gen<IndexModel> GenIndexModel(IReadOnlyList<string> columnNames) =>
        from name in GenSqlIdentifier()
        from isUnique in Arb.Generate<bool>()
        from isClustered in Arb.Generate<bool>()
        select new IndexModel
        {
            Name = name,
            IsUnique = isUnique,
            IsClustered = isClustered,
            KeyColumns = new List<IndexKeyColumn> { new IndexKeyColumn { Name = columnNames[0] } },
        };

    public static Gen<TableModel> GenTableModel() =>
        from tableName in GenSqlIdentifier()
        from columnCount in Gen.Choose(1, 5)
        from columnNames in Gen.ArrayOf(columnCount, GenSqlIdentifier())
            .Select(names => names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        from columns in Gen.Sequence(columnNames.Select(n =>
            from col in GenColumnModel()
            select new ColumnModel
            {
                Name = n,
                SqlType = col.SqlType,
                IsNullable = col.IsNullable,
                IsIdentity = col.IsIdentity,
            }))
        from hasConstraint in Arb.Generate<bool>()
        from constraint in hasConstraint && columnNames.Length > 0
            ? GenConstraintModel(columnNames).Select(c => (ConstraintModel?)c)
            : Gen.Constant((ConstraintModel?)null)
        from hasIndex in Arb.Generate<bool>()
        from index in hasIndex && columnNames.Length > 0
            ? GenIndexModel(columnNames).Select(ix => (IndexModel?)ix)
            : Gen.Constant((IndexModel?)null)
        select BuildTable("dbo", tableName, columns.ToList(), constraint, index);

    public static Gen<DatabaseModel> GenDatabaseModel() =>
        from tableCount in Gen.Choose(0, 4)
        from tables in Gen.ArrayOf(tableCount, GenTableModel())
            .Select(ts => ts
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray())
        select BuildDatabaseModel(tables);

    public static Gen<MigrationPlan> GenMigrationPlan() =>
        from opCount in Gen.Choose(0, 5)
        from ops in Gen.ArrayOf(opCount, GenSqlOperation())
        from skipCount in Gen.Choose(0, 3)
        from skips in Gen.ArrayOf(skipCount, GenSkippedItem())
        from proposalCount in Gen.Choose(0, 2)
        from proposals in Gen.ArrayOf(proposalCount, GenRebuildProposal())
        select new MigrationPlan(
            new PlanMetadata { Schema = "dbo", PlanFormatVersion = 1 },
            ops,
            skips,
            proposals);

    public static Gen<RebuildProposal> GenRebuildProposal() =>
        from tableName in GenSqlIdentifier()
        from stepCount in Gen.Choose(1, 4)
        from steps in Gen.ArrayOf(stepCount, GenRebuildStep())
        select new RebuildProposal
        {
            Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = tableName },
            Description = "Rebuild dbo." + tableName,
            Steps = steps,
            Script = string.Join("\nGO\n", steps.Select(s => s.Sql)),
        };

    private static Gen<RebuildStep> GenRebuildStep() =>
        from kind in Gen.Elements(
            RebuildStepKind.CreateShadowTable,
            RebuildStepKind.DropConstraintsOnOriginal,
            RebuildStepKind.DropIndexesOnOriginal,
            RebuildStepKind.CopyData,
            RebuildStepKind.RenameOriginalToOld,
            RebuildStepKind.RenameShadowToOriginal,
            RebuildStepKind.RecreateConstraints,
            RebuildStepKind.RecreateIndexes,
            RebuildStepKind.DropOldTable)
        from name in GenSqlIdentifier()
        select new RebuildStep
        {
            Kind = kind,
            Description = kind + " for " + name,
            Sql = "-- " + kind + " " + name,
        };

    private static Gen<SqlOperation> GenSqlOperation() =>
        from kind in Gen.Elements(
            OperationKind.CreateTable,
            OperationKind.AddColumn,
            OperationKind.RecreateConstraint,
            OperationKind.AddConstraint,
            OperationKind.RecreateIndex,
            OperationKind.CreateIndex,
            OperationKind.AddForeignKey,
            OperationKind.RecreateForeignKey,
            OperationKind.AddDescription,
            OperationKind.UpdateDescription,
            OperationKind.DropDescription,
            OperationKind.DropForeignKey,
            OperationKind.DropIndex,
            OperationKind.DropConstraint,
            OperationKind.DropColumn)
        from name in GenSqlIdentifier()
        select new SqlOperation
        {
            Kind = kind,
            Description = kind + " " + name,
            Sql = "-- " + kind + " " + name,
            Target = new SqlObjectRef
            {
                Type = SqlObjectType.Table,
                Schema = "dbo",
                Name = name,
            },
        };

    private static Gen<SkippedItem> GenSkippedItem() =>
        from reason in Gen.Elements(
            SkippedReason.DropNotSupported,
            SkippedReason.AlterNotSupported,
            SkippedReason.NotNullAddNotSupported)
        from name in GenSqlIdentifier()
        select new SkippedItem
        {
            Reason = reason,
            Message = reason + " for " + name,
            Target = new SqlObjectRef
            {
                Type = SqlObjectType.Table,
                Schema = "dbo",
                Name = name,
            },
        };

    private static ConstraintModel BuildConstraint(
        ConstraintKind kind, string name, IReadOnlyList<string> columnNames, bool isClustered)
    {
        var constraint = new ConstraintModel
        {
            Kind = kind,
            Name = name,
        };

        switch (kind)
        {
            case ConstraintKind.PrimaryKey:
            case ConstraintKind.Unique:
                constraint.Columns = new[] { columnNames[0] };
                constraint.IsClustered = isClustered;
                break;
            case ConstraintKind.Check:
                constraint.Definition = "(" + columnNames[0] + " IS NOT NULL)";
                break;
        }

        return constraint;
    }

    private static TableModel BuildTable(
        string schema,
        string tableName,
        List<ColumnModel> columns,
        ConstraintModel? constraint = null,
        IndexModel? index = null)
    {
        var table = new TableModel(schema, tableName);
        foreach (var col in columns)
        {
            table.Columns[IdentifierHelper.NormalizeNameKey(col.Name)] = col;
        }

        if (constraint != null)
        {
            table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
        }

        if (index != null)
        {
            table.Indexes[IdentifierHelper.NormalizeNameKey(index.Name)] = index;
        }

        return table;
    }

    private static DatabaseModel BuildDatabaseModel(TableModel[] tables)
    {
        var model = new DatabaseModel();
        foreach (var table in tables)
        {
            var key = IdentifierHelper.BuildTableKey(table.Schema, table.Name);
            model.Tables[key] = table;
        }

        return model;
    }
}
