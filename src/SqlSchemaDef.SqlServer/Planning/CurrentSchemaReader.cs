using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class CurrentSchemaReader
    {
        private const string TablesSql = @"
SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
ORDER BY t.name;";

        private const string ColumnsSql = @"
SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  c.column_id,
  c.name AS column_name,
  c.is_nullable,

  ty.name AS type_name,
  c.max_length,
  c.precision,
  c.scale,

  c.is_computed,

  CASE WHEN ic.object_id IS NULL THEN 0 ELSE 1 END AS is_identity,
  ic.seed_value,
  ic.increment_value
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.columns AS c
  ON c.object_id = t.object_id
JOIN sys.types AS ty
  ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.identity_columns AS ic
  ON ic.object_id = c.object_id
 AND ic.column_id = c.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
ORDER BY t.name, c.column_id;";

        private const string DefaultsSql = @"
SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,
  c.column_id,
  c.name AS column_name,
  dc.name AS default_name,
  dc.definition AS default_definition
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.columns AS c
  ON c.object_id = t.object_id
LEFT JOIN sys.default_constraints AS dc
  ON dc.parent_object_id = c.object_id
 AND dc.parent_column_id = c.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
  AND dc.object_id IS NOT NULL
ORDER BY t.name, c.column_id;";

        private const string KeyConstraintsSql = @"
SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  kc.name AS constraint_name,
  kc.type AS constraint_type,
  i.name AS index_name,
  ic.key_ordinal,
  c.name AS column_name
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.key_constraints AS kc
  ON kc.parent_object_id = t.object_id
JOIN sys.indexes AS i
  ON i.object_id = t.object_id
 AND i.index_id = kc.unique_index_id
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id
 AND ic.index_id = i.index_id
 AND ic.key_ordinal > 0
JOIN sys.columns AS c
  ON c.object_id = t.object_id
 AND c.column_id = ic.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
  AND kc.type IN ('PK', 'UQ')
ORDER BY t.name, kc.name, ic.key_ordinal;";

        private const string CheckConstraintsSql = @"
SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  cc.name AS constraint_name,
  cc.definition AS check_definition,
  cc.is_disabled,
  cc.is_not_trusted
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.check_constraints AS cc
  ON cc.parent_object_id = t.object_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
ORDER BY t.name, cc.name;";

        private const string IndexesSql = @"
SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  i.index_id,
  i.name AS index_name,
  i.is_unique,
  i.type_desc,
  i.is_primary_key,
  i.is_unique_constraint,

  ic.key_ordinal,
  ic.is_included_column,
  ic.is_descending_key,
  c.name AS column_name
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.indexes AS i
  ON i.object_id = t.object_id
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id
 AND ic.index_id = i.index_id
JOIN sys.columns AS c
  ON c.object_id = t.object_id
 AND c.column_id = ic.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
  AND i.name IS NOT NULL
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
ORDER BY t.name, i.name, ic.is_included_column, ic.key_ordinal, c.name;";

        private const string ForeignKeysSql = @"
SELECT
  ps.name AS parent_schema_name,
  pt.name AS parent_table_name,
  pt.object_id AS parent_object_id,

  fk.name AS foreign_key_name,

  rs.name AS referenced_schema_name,
  rt.name AS referenced_table_name,
  rt.object_id AS referenced_object_id,

  fkc.constraint_column_id AS ordinal,
  pc.name AS parent_column_name,
  rc.name AS referenced_column_name,

  fk.delete_referential_action_desc,
  fk.update_referential_action_desc
FROM sys.foreign_keys AS fk
JOIN sys.tables AS pt
  ON pt.object_id = fk.parent_object_id
JOIN sys.schemas AS ps
  ON ps.schema_id = pt.schema_id
JOIN sys.tables AS rt
  ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas AS rs
  ON rs.schema_id = rt.schema_id
JOIN sys.foreign_key_columns AS fkc
  ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc
  ON pc.object_id = pt.object_id
 AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc
  ON rc.object_id = rt.object_id
 AND rc.column_id = fkc.referenced_column_id
WHERE ps.name = @schema
  AND pt.is_ms_shipped = 0
ORDER BY pt.name, fk.name, fkc.constraint_column_id;";

        public async Task<DatabaseModel> ReadAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken = default)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(schema))
                throw new ArgumentException("Schema is required.", nameof(schema));

            var tables = await ReadTablesAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            var columns = await ReadColumnsAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            var defaults = await ReadDefaultsAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            var keyConstraints = await ReadKeyConstraintsAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            var checkConstraints = await ReadCheckConstraintsAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            var indexes = await ReadIndexesAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            var foreignKeys = await ReadForeignKeysAsync(connection, schema, cancellationToken).ConfigureAwait(false);

            return BuildModel(tables, columns, defaults, keyConstraints, checkConstraints, foreignKeys, indexes);
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
        }

        internal sealed class DefaultRow
        {
            public int ObjectId { get; set; }
            public int ColumnId { get; set; }
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
        }

        internal sealed class IndexRow
        {
            public int ObjectId { get; set; }
            public string IndexName { get; set; }
            public bool IsUnique { get; set; }
            public int KeyOrdinal { get; set; }
            public bool IsIncludedColumn { get; set; }
            public bool IsDescendingKey { get; set; }
            public string ColumnName { get; set; }
        }

        private static async Task<List<TableRow>> ReadTablesAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, TablesSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<TableRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new TableRow
                    {
                        SchemaName = reader.GetString(0),
                        TableName = reader.GetString(1),
                        ObjectId = reader.GetInt32(2),
                    });
                }
                return results;
            }
        }

        private static async Task<List<ColumnRow>> ReadColumnsAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, ColumnsSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<ColumnRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new ColumnRow
                    {
                        ObjectId = reader.GetInt32(2),
                        ColumnId = reader.GetInt32(3),
                        ColumnName = reader.GetString(4),
                        IsNullable = reader.GetBoolean(5),
                        TypeName = reader.GetString(6),
                        MaxLength = reader.GetInt16(7),
                        Precision = reader.GetByte(8),
                        Scale = reader.GetByte(9),
                        IsComputed = reader.GetBoolean(10),
                        IsIdentity = reader.GetInt32(11) != 0,
                    });
                }
                return results;
            }
        }

        private static async Task<List<DefaultRow>> ReadDefaultsAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, DefaultsSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<DefaultRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new DefaultRow
                    {
                        ObjectId = reader.GetInt32(2),
                        ColumnId = reader.GetInt32(3),
                        DefaultDefinition = reader.GetString(6),
                    });
                }
                return results;
            }
        }

        private static async Task<List<KeyConstraintRow>> ReadKeyConstraintsAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, KeyConstraintsSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<KeyConstraintRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new KeyConstraintRow
                    {
                        ObjectId = reader.GetInt32(2),
                        ConstraintName = reader.GetString(3),
                        ConstraintType = reader.GetString(4),
                        KeyOrdinal = reader.GetInt32(6),
                        ColumnName = reader.GetString(7),
                    });
                }
                return results;
            }
        }

        private static async Task<List<CheckConstraintRow>> ReadCheckConstraintsAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, CheckConstraintsSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<CheckConstraintRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new CheckConstraintRow
                    {
                        ObjectId = reader.GetInt32(2),
                        ConstraintName = reader.GetString(3),
                        Definition = reader.GetString(4),
                    });
                }
                return results;
            }
        }

        private static async Task<List<IndexRow>> ReadIndexesAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, IndexesSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<IndexRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new IndexRow
                    {
                        ObjectId = reader.GetInt32(2),
                        IndexName = reader.GetString(4),
                        IsUnique = reader.GetBoolean(5),
                        KeyOrdinal = reader.GetInt32(9),
                        IsIncludedColumn = reader.GetBoolean(10),
                        IsDescendingKey = reader.GetBoolean(11),
                        ColumnName = reader.GetString(12),
                    });
                }
                return results;
            }
        }

        private static async Task<List<ForeignKeyRow>> ReadForeignKeysAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            using (var command = CreateCommand(connection, ForeignKeysSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var results = new List<ForeignKeyRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new ForeignKeyRow
                    {
                        ParentObjectId = reader.GetInt32(2),
                        ConstraintName = reader.GetString(3),
                        ReferencedSchemaName = reader.GetString(4),
                        ReferencedTableName = reader.GetString(5),
                        Ordinal = reader.GetInt32(7),
                        ParentColumnName = reader.GetString(8),
                        ReferencedColumnName = reader.GetString(9),
                    });
                }
                return results;
            }
        }

        private static SqlCommand CreateCommand(SqlConnection connection, string sql, string schema)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
            return command;
        }

        internal static DatabaseModel BuildModel(
            IEnumerable<TableRow> tables,
            IEnumerable<ColumnRow> columns,
            IEnumerable<DefaultRow> defaults = null,
            IEnumerable<KeyConstraintRow> keyConstraints = null,
            IEnumerable<CheckConstraintRow> checkConstraints = null,
            IEnumerable<ForeignKeyRow> foreignKeys = null,
            IEnumerable<IndexRow> indexes = null)
        {
            if (tables == null)
                throw new ArgumentNullException(nameof(tables));
            if (columns == null)
                throw new ArgumentNullException(nameof(columns));

            var model = new DatabaseModel();
            var tableMap = new Dictionary<int, TableModel>();
            var defaultMap = BuildDefaultMap(defaults);
            var keyConstraintGroups = BuildKeyConstraintGroups(keyConstraints);
            var checkConstraintGroups = BuildCheckConstraintGroups(checkConstraints);
            var foreignKeyGroups = BuildForeignKeyGroups(foreignKeys);
            var indexGroups = BuildIndexGroups(indexes);

            foreach (var table in tables)
            {
                if (table == null)
                    continue;

                if (!string.Equals(table.SchemaName, "dbo", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Current schema reader only supports dbo.");
                }

                var tableModel = model.GetOrAddTable("dbo", table.TableName);
                tableMap[table.ObjectId] = tableModel;
            }

            foreach (var column in columns)
            {
                if (column == null)
                    continue;

                if (!tableMap.TryGetValue(column.ObjectId, out var table))
                {
                    throw new InvalidOperationException("Column row references an unknown table.");
                }

                var columnModel = new ColumnModel
                {
                    Name = column.ColumnName,
                    SqlType = SqlTypeFormatter.Format(
                        column.TypeName,
                        column.MaxLength,
                        column.Precision,
                        column.Scale),
                    IsNullable = column.IsNullable,
                    IsIdentity = column.IsIdentity,
                    DefaultExpression = GetDefaultDefinition(defaultMap, column.ObjectId, column.ColumnId),
                    IsFromAlterAdd = false,
                    UnsupportedFeature = column.IsComputed ? "ComputedColumn" : null,
                };

                table.Columns[IdentifierHelper.NormalizeNameKey(columnModel.Name)] = columnModel;
            }

            ApplyKeyConstraints(tableMap, keyConstraintGroups);
            ApplyCheckConstraints(tableMap, checkConstraintGroups);
            ApplyForeignKeys(tableMap, foreignKeyGroups);
            ApplyIndexes(tableMap, indexGroups);

            return model;
        }

        private static Dictionary<(int ObjectId, int ColumnId), string> BuildDefaultMap(IEnumerable<DefaultRow> defaults)
        {
            var map = new Dictionary<(int ObjectId, int ColumnId), string>();
            if (defaults == null)
            {
                return map;
            }

            foreach (var item in defaults)
            {
                if (item == null)
                    continue;

                map[(item.ObjectId, item.ColumnId)] = item.DefaultDefinition;
            }

            return map;
        }

        private static string GetDefaultDefinition(
            Dictionary<(int ObjectId, int ColumnId), string> map,
            int objectId,
            int columnId)
        {
            if (map.TryGetValue((objectId, columnId), out var definition))
            {
                return definition;
            }

            return null;
        }

        private static Dictionary<(int ObjectId, string ConstraintName), List<KeyConstraintRow>> BuildKeyConstraintGroups(
            IEnumerable<KeyConstraintRow> keyConstraints)
        {
            var groups = new Dictionary<(int ObjectId, string ConstraintName), List<KeyConstraintRow>>();
            if (keyConstraints == null)
            {
                return groups;
            }

            foreach (var item in keyConstraints)
            {
                if (item == null)
                    continue;

                var key = (item.ObjectId, item.ConstraintName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<KeyConstraintRow>();
                    groups[key] = list;
                }
                list.Add(item);
            }

            return groups;
        }

        private static void ApplyKeyConstraints(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, string ConstraintName), List<KeyConstraintRow>> groups)
        {
            foreach (var entry in groups)
            {
                if (!tableMap.TryGetValue(entry.Key.ObjectId, out var table))
                {
                    throw new InvalidOperationException("Key constraint references an unknown table.");
                }

                var constraintName = entry.Key.ConstraintName;
                if (string.IsNullOrWhiteSpace(constraintName))
                {
                    throw new InvalidOperationException("Key constraint name is required.");
                }

                var rows = entry.Value;
                rows.Sort((left, right) => left.KeyOrdinal.CompareTo(right.KeyOrdinal));

                var kind = ResolveKeyConstraintKind(rows[0].ConstraintType);
                var columns = new List<string>(rows.Count);
                foreach (var row in rows)
                {
                    columns.Add(row.ColumnName);
                }

                var constraint = new ConstraintModel
                {
                    Kind = kind,
                    Name = constraintName,
                    Columns = columns,
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
        }

        private static ConstraintKind ResolveKeyConstraintKind(string constraintType)
        {
            if (string.Equals(constraintType, "PK", StringComparison.OrdinalIgnoreCase))
            {
                return ConstraintKind.PrimaryKey;
            }

            if (string.Equals(constraintType, "UQ", StringComparison.OrdinalIgnoreCase))
            {
                return ConstraintKind.Unique;
            }

            throw new InvalidOperationException("Unsupported key constraint type.");
        }

        private static Dictionary<(int ObjectId, string ConstraintName), List<CheckConstraintRow>> BuildCheckConstraintGroups(
            IEnumerable<CheckConstraintRow> checkConstraints)
        {
            var groups = new Dictionary<(int ObjectId, string ConstraintName), List<CheckConstraintRow>>();
            if (checkConstraints == null)
            {
                return groups;
            }

            foreach (var item in checkConstraints)
            {
                if (item == null)
                    continue;

                var key = (item.ObjectId, item.ConstraintName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<CheckConstraintRow>();
                    groups[key] = list;
                }
                list.Add(item);
            }

            return groups;
        }

        private static void ApplyCheckConstraints(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, string ConstraintName), List<CheckConstraintRow>> groups)
        {
            foreach (var entry in groups)
            {
                if (!tableMap.TryGetValue(entry.Key.ObjectId, out var table))
                {
                    throw new InvalidOperationException("Check constraint references an unknown table.");
                }

                var constraintName = entry.Key.ConstraintName;
                if (string.IsNullOrWhiteSpace(constraintName))
                {
                    throw new InvalidOperationException("Check constraint name is required.");
                }

                var row = entry.Value[0];
                var constraint = new ConstraintModel
                {
                    Kind = ConstraintKind.Check,
                    Name = constraintName,
                    Definition = row.Definition,
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
        }

        private static Dictionary<(int ParentObjectId, string ConstraintName), List<ForeignKeyRow>> BuildForeignKeyGroups(
            IEnumerable<ForeignKeyRow> foreignKeys)
        {
            var groups = new Dictionary<(int ParentObjectId, string ConstraintName), List<ForeignKeyRow>>();
            if (foreignKeys == null)
            {
                return groups;
            }

            foreach (var item in foreignKeys)
            {
                if (item == null)
                    continue;

                var key = (item.ParentObjectId, item.ConstraintName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<ForeignKeyRow>();
                    groups[key] = list;
                }
                list.Add(item);
            }

            return groups;
        }

        private static void ApplyForeignKeys(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ParentObjectId, string ConstraintName), List<ForeignKeyRow>> groups)
        {
            foreach (var entry in groups)
            {
                if (!tableMap.TryGetValue(entry.Key.ParentObjectId, out var table))
                {
                    throw new InvalidOperationException("Foreign key references an unknown table.");
                }

                var constraintName = entry.Key.ConstraintName;
                if (string.IsNullOrWhiteSpace(constraintName))
                {
                    throw new InvalidOperationException("Foreign key name is required.");
                }

                var rows = entry.Value;
                rows.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));

                var referenceSchema = string.IsNullOrWhiteSpace(rows[0].ReferencedSchemaName)
                    ? "dbo"
                    : rows[0].ReferencedSchemaName;

                var parentColumns = new List<string>(rows.Count);
                var referencedColumns = new List<string>(rows.Count);
                foreach (var row in rows)
                {
                    parentColumns.Add(row.ParentColumnName);
                    referencedColumns.Add(row.ReferencedColumnName);
                }

                var constraint = new ConstraintModel
                {
                    Kind = ConstraintKind.ForeignKey,
                    Name = constraintName,
                    Columns = parentColumns,
                    ReferenceSchema = referenceSchema,
                    ReferenceTable = rows[0].ReferencedTableName,
                    ReferenceColumns = referencedColumns,
                };

                if (!string.Equals(referenceSchema, "dbo", StringComparison.OrdinalIgnoreCase))
                {
                    constraint.UnsupportedFeature = "ForeignKeyReferenceSchema";
                }

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
        }

        private static Dictionary<(int ObjectId, string IndexName), List<IndexRow>> BuildIndexGroups(
            IEnumerable<IndexRow> indexes)
        {
            var groups = new Dictionary<(int ObjectId, string IndexName), List<IndexRow>>();
            if (indexes == null)
            {
                return groups;
            }

            foreach (var item in indexes)
            {
                if (item == null)
                    continue;

                var key = (item.ObjectId, item.IndexName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<IndexRow>();
                    groups[key] = list;
                }
                list.Add(item);
            }

            return groups;
        }

        private static void ApplyIndexes(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, string IndexName), List<IndexRow>> groups)
        {
            foreach (var entry in groups)
            {
                if (!tableMap.TryGetValue(entry.Key.ObjectId, out var table))
                {
                    throw new InvalidOperationException("Index references an unknown table.");
                }

                var indexName = entry.Key.IndexName;
                if (string.IsNullOrWhiteSpace(indexName))
                {
                    throw new InvalidOperationException("Index name is required.");
                }

                var rows = entry.Value;
                rows.Sort((left, right) => left.KeyOrdinal.CompareTo(right.KeyOrdinal));

                var isUnique = false;
                var keyColumns = new List<string>();
                string unsupportedFeature = null;
                foreach (var row in rows)
                {
                    isUnique = row.IsUnique;

                    if (row.IsDescendingKey)
                    {
                        unsupportedFeature = unsupportedFeature ?? "IndexSortOrder";
                    }

                    if (row.IsIncludedColumn)
                    {
                        unsupportedFeature = unsupportedFeature ?? "IndexInclude";
                        continue;
                    }

                    if (row.KeyOrdinal <= 0)
                    {
                        continue;
                    }

                    keyColumns.Add(row.ColumnName);
                }

                var index = new IndexModel
                {
                    Name = indexName,
                    IsUnique = isUnique,
                    KeyColumns = keyColumns,
                    UnsupportedFeature = unsupportedFeature,
                };

                table.Indexes[IdentifierHelper.NormalizeNameKey(index.Name)] = index;
            }
        }
    }
}
