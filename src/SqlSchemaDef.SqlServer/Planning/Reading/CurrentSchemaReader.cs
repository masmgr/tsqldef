using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class CurrentSchemaReader
    {
        public static async Task<DatabaseModel> ReadAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken = default)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(schema))
                throw new ArgumentException("Schema is required.", nameof(schema));

            var readResult = await CurrentSchemaCatalogReader
                .ReadAsync(connection, schema, cancellationToken)
                .ConfigureAwait(false);

            return BuildModel(
                readResult.Tables,
                readResult.Columns,
                readResult.Defaults,
                readResult.KeyConstraints,
                readResult.CheckConstraints,
                readResult.ForeignKeys,
                readResult.Indexes,
                readResult.ExtendedProperties);
        }

        internal sealed class TableRow
        {
            public string SchemaName { get; set; }
            public string TableName { get; set; }
            public int ObjectId { get; set; }
        }

        internal sealed class ColumnRow
        {
            public int ObjectId { get; set; }
            public int ColumnId { get; set; }
            public string ColumnName { get; set; }
            public bool IsNullable { get; set; }
            public string TypeName { get; set; }
            public short MaxLength { get; set; }
            public byte Precision { get; set; }
            public byte Scale { get; set; }
            public bool IsComputed { get; set; }
            public bool IsIdentity { get; set; }
            public string Collation { get; set; }
        }

        internal sealed class DefaultRow
        {
            public int ObjectId { get; set; }
            public int ColumnId { get; set; }
            public string ColumnName { get; set; }
            public string DefaultName { get; set; }
            public string DefaultDefinition { get; set; }
        }

        internal sealed class KeyConstraintRow
        {
            public int ObjectId { get; set; }
            public string ConstraintName { get; set; }
            public string ConstraintType { get; set; }
            public int KeyOrdinal { get; set; }
            public string ColumnName { get; set; }
        }

        internal sealed class CheckConstraintRow
        {
            public int ObjectId { get; set; }
            public string ConstraintName { get; set; }
            public string Definition { get; set; }
        }

        internal sealed class ForeignKeyRow
        {
            public int ParentObjectId { get; set; }
            public string ConstraintName { get; set; }
            public string ReferencedSchemaName { get; set; }
            public string ReferencedTableName { get; set; }
            public int Ordinal { get; set; }
            public string ParentColumnName { get; set; }
            public string ReferencedColumnName { get; set; }
            public string DeleteAction { get; set; }
            public string UpdateAction { get; set; }
        }

        internal sealed class IndexRow
        {
            public int ObjectId { get; set; }
            public string IndexName { get; set; }
            public bool IsUnique { get; set; }
            public bool IsClustered { get; set; }
            public int KeyOrdinal { get; set; }
            public bool IsIncludedColumn { get; set; }
            public bool IsDescendingKey { get; set; }
            public string ColumnName { get; set; }
            public string FilterPredicate { get; set; }
            public int FillFactor { get; set; }
            public bool IsPadded { get; set; }
            public bool IgnoreDupKey { get; set; }
            public bool AllowRowLocks { get; set; }
            public bool AllowPageLocks { get; set; }
            public bool NoRecompute { get; set; }
        }

        internal sealed class ExtendedPropertyRow
        {
            public int MajorId { get; set; }
            public int MinorId { get; set; }
            public string PropertyValue { get; set; }
        }

        internal static DatabaseModel BuildModel(
            IEnumerable<TableRow> tables,
            IEnumerable<ColumnRow> columns,
            IEnumerable<DefaultRow> defaults = null,
            IEnumerable<KeyConstraintRow> keyConstraints = null,
            IEnumerable<CheckConstraintRow> checkConstraints = null,
            IEnumerable<ForeignKeyRow> foreignKeys = null,
            IEnumerable<IndexRow> indexes = null,
            IEnumerable<ExtendedPropertyRow> extendedProperties = null)
        {
            return CurrentSchemaModelBuilder.Build(
                tables,
                columns,
                defaults,
                keyConstraints,
                checkConstraints,
                foreignKeys,
                indexes,
                extendedProperties);
        }
    }
}
