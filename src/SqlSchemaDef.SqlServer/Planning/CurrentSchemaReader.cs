using System;
using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class CurrentSchemaReader
    {
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

        internal static DatabaseModel BuildModel(
            IEnumerable<TableRow> tables,
            IEnumerable<ColumnRow> columns,
            IEnumerable<DefaultRow> defaults = null,
            IEnumerable<KeyConstraintRow> keyConstraints = null,
            IEnumerable<CheckConstraintRow> checkConstraints = null,
            IEnumerable<ForeignKeyRow> foreignKeys = null)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var model = new DatabaseModel();
            var tableMap = new Dictionary<int, TableModel>();
            var defaultMap = BuildDefaultMap(defaults);
            var keyConstraintGroups = BuildKeyConstraintGroups(keyConstraints);
            var checkConstraintGroups = BuildCheckConstraintGroups(checkConstraints);
            var foreignKeyGroups = BuildForeignKeyGroups(foreignKeys);

            foreach (var table in tables)
            {
                if (table == null) continue;

                if (!string.Equals(table.SchemaName, "dbo", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Current schema reader only supports dbo.");
                }

                var tableModel = model.GetOrAddTable("dbo", table.TableName);
                tableMap[table.ObjectId] = tableModel;
            }

            foreach (var column in columns)
            {
                if (column == null) continue;

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
                };

                table.Columns[IdentifierHelper.NormalizeNameKey(columnModel.Name)] = columnModel;
            }

            ApplyKeyConstraints(tableMap, keyConstraintGroups);
            ApplyCheckConstraints(tableMap, checkConstraintGroups);
            ApplyForeignKeys(tableMap, foreignKeyGroups);

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
                if (item == null) continue;

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
                if (item == null) continue;

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
                if (item == null) continue;

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
                if (item == null) continue;

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

                var referenceSchema = rows[0].ReferencedSchemaName;
                if (!string.Equals(referenceSchema, "dbo", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Foreign key references unsupported schema.");
                }

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
                    ReferenceSchema = "dbo",
                    ReferenceTable = rows[0].ReferencedTableName,
                    ReferenceColumns = referencedColumns,
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
        }
    }
}
