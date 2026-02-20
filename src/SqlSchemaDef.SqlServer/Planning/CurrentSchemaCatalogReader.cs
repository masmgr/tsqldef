using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class CurrentSchemaCatalogReader
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

        private const string ReadAllSql = TablesSql + "\n" +
                                          ColumnsSql + "\n" +
                                          DefaultsSql + "\n" +
                                          KeyConstraintsSql + "\n" +
                                          CheckConstraintsSql + "\n" +
                                          IndexesSql + "\n" +
                                          ForeignKeysSql;

        internal static async Task<Result> ReadAsync(
            SqlConnection connection,
            string schema,
            CancellationToken cancellationToken)
        {
            List<CurrentSchemaReader.TableRow> tables;
            List<CurrentSchemaReader.ColumnRow> columns;
            List<CurrentSchemaReader.DefaultRow> defaults;
            List<CurrentSchemaReader.KeyConstraintRow> keyConstraints;
            List<CurrentSchemaReader.CheckConstraintRow> checkConstraints;
            List<CurrentSchemaReader.IndexRow> indexes;
            List<CurrentSchemaReader.ForeignKeyRow> foreignKeys;

            using (var command = CreateCommand(connection, ReadAllSql, schema))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                tables = await ReadTablesResultSetAsync(reader, cancellationToken).ConfigureAwait(false);

                await MoveToNextResultSetAsync(reader, "columns", cancellationToken).ConfigureAwait(false);
                columns = await ReadColumnsResultSetAsync(reader, cancellationToken).ConfigureAwait(false);

                await MoveToNextResultSetAsync(reader, "defaults", cancellationToken).ConfigureAwait(false);
                defaults = await ReadDefaultsResultSetAsync(reader, cancellationToken).ConfigureAwait(false);

                await MoveToNextResultSetAsync(reader, "key constraints", cancellationToken).ConfigureAwait(false);
                keyConstraints = await ReadKeyConstraintsResultSetAsync(reader, cancellationToken).ConfigureAwait(false);

                await MoveToNextResultSetAsync(reader, "check constraints", cancellationToken).ConfigureAwait(false);
                checkConstraints = await ReadCheckConstraintsResultSetAsync(reader, cancellationToken).ConfigureAwait(false);

                await MoveToNextResultSetAsync(reader, "indexes", cancellationToken).ConfigureAwait(false);
                indexes = await ReadIndexesResultSetAsync(reader, cancellationToken).ConfigureAwait(false);

                await MoveToNextResultSetAsync(reader, "foreign keys", cancellationToken).ConfigureAwait(false);
                foreignKeys = await ReadForeignKeysResultSetAsync(reader, cancellationToken).ConfigureAwait(false);
            }

            return new Result(
                tables,
                columns,
                defaults,
                keyConstraints,
                checkConstraints,
                foreignKeys,
                indexes);
        }

        private static int GetInt32(SqlDataReader reader, int ordinal)
        {
            var value = reader.GetValue(ordinal);
            if (value is int intValue)
                return intValue;
            if (value is short shortValue)
                return shortValue;
            if (value is byte byteValue)
                return byteValue;
            if (value is long longValue)
                return checked((int)longValue);

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static async Task<List<CurrentSchemaReader.TableRow>> ReadTablesResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.TableRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.TableRow
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    ObjectId = reader.GetInt32(2),
                });
            }

            return results;
        }

        private static async Task<List<CurrentSchemaReader.ColumnRow>> ReadColumnsResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.ColumnRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.ColumnRow
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

        private static async Task<List<CurrentSchemaReader.DefaultRow>> ReadDefaultsResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.DefaultRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.DefaultRow
                {
                    ObjectId = reader.GetInt32(2),
                    ColumnId = reader.GetInt32(3),
                    ColumnName = reader.GetString(4),
                    DefaultName = reader.GetString(5),
                    DefaultDefinition = reader.GetString(6),
                });
            }

            return results;
        }

        private static async Task<List<CurrentSchemaReader.KeyConstraintRow>> ReadKeyConstraintsResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.KeyConstraintRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.KeyConstraintRow
                {
                    ObjectId = reader.GetInt32(2),
                    ConstraintName = reader.GetString(3),
                    ConstraintType = reader.GetString(4),
                    KeyOrdinal = GetInt32(reader, 6),
                    ColumnName = reader.GetString(7),
                });
            }

            return results;
        }

        private static async Task<List<CurrentSchemaReader.CheckConstraintRow>> ReadCheckConstraintsResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.CheckConstraintRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.CheckConstraintRow
                {
                    ObjectId = reader.GetInt32(2),
                    ConstraintName = reader.GetString(3),
                    Definition = reader.GetString(4),
                });
            }

            return results;
        }

        private static async Task<List<CurrentSchemaReader.IndexRow>> ReadIndexesResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.IndexRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.IndexRow
                {
                    ObjectId = reader.GetInt32(2),
                    IndexName = reader.GetString(4),
                    IsUnique = reader.GetBoolean(5),
                    KeyOrdinal = GetInt32(reader, 9),
                    IsIncludedColumn = reader.GetBoolean(10),
                    IsDescendingKey = reader.GetBoolean(11),
                    ColumnName = reader.GetString(12),
                });
            }

            return results;
        }

        private static async Task<List<CurrentSchemaReader.ForeignKeyRow>> ReadForeignKeysResultSetAsync(
            SqlDataReader reader,
            CancellationToken cancellationToken)
        {
            var results = new List<CurrentSchemaReader.ForeignKeyRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new CurrentSchemaReader.ForeignKeyRow
                {
                    ParentObjectId = reader.GetInt32(2),
                    ConstraintName = reader.GetString(3),
                    ReferencedSchemaName = reader.GetString(4),
                    ReferencedTableName = reader.GetString(5),
                    Ordinal = reader.GetInt32(7),
                    ParentColumnName = reader.GetString(8),
                    ReferencedColumnName = reader.GetString(9),
                    DeleteAction = NormalizeForeignKeyAction(reader.GetString(10)),
                    UpdateAction = NormalizeForeignKeyAction(reader.GetString(11)),
                });
            }

            return results;
        }

        private static async Task MoveToNextResultSetAsync(
            SqlDataReader reader,
            string resultSetName,
            CancellationToken cancellationToken)
        {
            if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Expected schema result set for " + resultSetName + ".");
            }
        }

        private static string NormalizeForeignKeyAction(string actionDesc)
        {
            if (string.IsNullOrWhiteSpace(actionDesc) ||
                string.Equals(actionDesc, "NO_ACTION", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(actionDesc, "CASCADE", StringComparison.OrdinalIgnoreCase))
            {
                return "CASCADE";
            }

            if (string.Equals(actionDesc, "SET_NULL", StringComparison.OrdinalIgnoreCase))
            {
                return "SET NULL";
            }

            if (string.Equals(actionDesc, "SET_DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                return "SET DEFAULT";
            }

            return null;
        }

        private static SqlCommand CreateCommand(SqlConnection connection, string sql, string schema)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
            return command;
        }

        internal sealed class Result
        {
            public Result(
                List<CurrentSchemaReader.TableRow> tables,
                List<CurrentSchemaReader.ColumnRow> columns,
                List<CurrentSchemaReader.DefaultRow> defaults,
                List<CurrentSchemaReader.KeyConstraintRow> keyConstraints,
                List<CurrentSchemaReader.CheckConstraintRow> checkConstraints,
                List<CurrentSchemaReader.ForeignKeyRow> foreignKeys,
                List<CurrentSchemaReader.IndexRow> indexes)
            {
                Tables = tables;
                Columns = columns;
                Defaults = defaults;
                KeyConstraints = keyConstraints;
                CheckConstraints = checkConstraints;
                ForeignKeys = foreignKeys;
                Indexes = indexes;
            }

            public List<CurrentSchemaReader.TableRow> Tables { get; }
            public List<CurrentSchemaReader.ColumnRow> Columns { get; }
            public List<CurrentSchemaReader.DefaultRow> Defaults { get; }
            public List<CurrentSchemaReader.KeyConstraintRow> KeyConstraints { get; }
            public List<CurrentSchemaReader.CheckConstraintRow> CheckConstraints { get; }
            public List<CurrentSchemaReader.ForeignKeyRow> ForeignKeys { get; }
            public List<CurrentSchemaReader.IndexRow> Indexes { get; }
        }
    }
}
