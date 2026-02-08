using System;
using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class CurrentSchemaModelBuilder
    {
        internal static DatabaseModel Build(
            IEnumerable<CurrentSchemaReader.TableRow> tables,
            IEnumerable<CurrentSchemaReader.ColumnRow> columns,
            IEnumerable<CurrentSchemaReader.DefaultRow> defaults = null,
            IEnumerable<CurrentSchemaReader.KeyConstraintRow> keyConstraints = null,
            IEnumerable<CurrentSchemaReader.CheckConstraintRow> checkConstraints = null,
            IEnumerable<CurrentSchemaReader.ForeignKeyRow> foreignKeys = null,
            IEnumerable<CurrentSchemaReader.IndexRow> indexes = null)
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

        private static Dictionary<(int ObjectId, int ColumnId), string> BuildDefaultMap(
            IEnumerable<CurrentSchemaReader.DefaultRow> defaults)
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

        private static Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.KeyConstraintRow>> BuildKeyConstraintGroups(
            IEnumerable<CurrentSchemaReader.KeyConstraintRow> keyConstraints)
        {
            var groups = new Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.KeyConstraintRow>>();
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
                    list = new List<CurrentSchemaReader.KeyConstraintRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        private static void ApplyKeyConstraints(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.KeyConstraintRow>> groups)
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

        private static Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.CheckConstraintRow>> BuildCheckConstraintGroups(
            IEnumerable<CurrentSchemaReader.CheckConstraintRow> checkConstraints)
        {
            var groups = new Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.CheckConstraintRow>>();
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
                    list = new List<CurrentSchemaReader.CheckConstraintRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        private static void ApplyCheckConstraints(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.CheckConstraintRow>> groups)
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

        private static Dictionary<(int ParentObjectId, string ConstraintName), List<CurrentSchemaReader.ForeignKeyRow>> BuildForeignKeyGroups(
            IEnumerable<CurrentSchemaReader.ForeignKeyRow> foreignKeys)
        {
            var groups = new Dictionary<(int ParentObjectId, string ConstraintName), List<CurrentSchemaReader.ForeignKeyRow>>();
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
                    list = new List<CurrentSchemaReader.ForeignKeyRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        private static void ApplyForeignKeys(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ParentObjectId, string ConstraintName), List<CurrentSchemaReader.ForeignKeyRow>> groups)
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

        private static Dictionary<(int ObjectId, string IndexName), List<CurrentSchemaReader.IndexRow>> BuildIndexGroups(
            IEnumerable<CurrentSchemaReader.IndexRow> indexes)
        {
            var groups = new Dictionary<(int ObjectId, string IndexName), List<CurrentSchemaReader.IndexRow>>();
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
                    list = new List<CurrentSchemaReader.IndexRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        private static void ApplyIndexes(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, string IndexName), List<CurrentSchemaReader.IndexRow>> groups)
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
                var keyColumns = new List<IndexKeyColumn>();
                var includeColumns = new List<string>();
                foreach (var row in rows)
                {
                    isUnique = row.IsUnique;

                    if (row.IsIncludedColumn)
                    {
                        includeColumns.Add(row.ColumnName);
                        continue;
                    }

                    if (row.KeyOrdinal <= 0)
                    {
                        continue;
                    }

                    keyColumns.Add(new IndexKeyColumn
                    {
                        Name = row.ColumnName,
                        IsDescending = row.IsDescendingKey,
                    });
                }

                var index = new IndexModel
                {
                    Name = indexName,
                    IsUnique = isUnique,
                    KeyColumns = keyColumns,
                    IncludeColumns = includeColumns.Count == 0 ? null : includeColumns,
                };

                table.Indexes[IdentifierHelper.NormalizeNameKey(index.Name)] = index;
            }
        }
    }
}
