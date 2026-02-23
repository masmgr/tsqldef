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
            IEnumerable<CurrentSchemaReader.IndexRow> indexes = null,
            IEnumerable<CurrentSchemaReader.ExtendedPropertyRow> extendedProperties = null,
            string databaseCollation = null)
        {
            if (tables == null)
                throw new ArgumentNullException(nameof(tables));
            if (columns == null)
                throw new ArgumentNullException(nameof(columns));
            _ = databaseCollation;

            var model = new DatabaseModel();
            var tableMap = new Dictionary<int, TableModel>();
            var columnIdMap = new Dictionary<(int ObjectId, int ColumnId), ColumnModel>();
            var defaultMap = CurrentSchemaRowGrouping.BuildDefaultMap(defaults);
            var keyConstraintGroups = CurrentSchemaRowGrouping.BuildKeyConstraintGroups(keyConstraints);
            var checkConstraintGroups = CurrentSchemaRowGrouping.BuildCheckConstraintGroups(checkConstraints);
            var foreignKeyGroups = CurrentSchemaRowGrouping.BuildForeignKeyGroups(foreignKeys);
            var indexGroups = CurrentSchemaRowGrouping.BuildIndexGroups(indexes);

            foreach (var table in tables)
            {
                if (table == null)
                    continue;

                var tableModel = model.GetOrAddTable(table.SchemaName, table.TableName);
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
                    DefaultExpression = CurrentSchemaModelBuilderHelpers.GetDefaultDefinition(defaultMap, column.ObjectId, column.ColumnId),
                    IsFromAlterAdd = false,
                    UnsupportedFeature = column.IsComputed ? "ComputedColumn" : null,
                    Collation = column.Collation,
                };

                table.Columns[IdentifierHelper.NormalizeNameKey(columnModel.Name)] = columnModel;
                columnIdMap[(column.ObjectId, column.ColumnId)] = columnModel;
            }

            ApplyKeyConstraints(tableMap, keyConstraintGroups);
            ApplyCheckConstraints(tableMap, checkConstraintGroups);
            ApplyForeignKeys(tableMap, foreignKeyGroups);
            ApplyDefaultConstraints(tableMap, defaults);
            ApplyIndexes(tableMap, indexGroups);
            ApplyExtendedProperties(tableMap, columnIdMap, extendedProperties);

            return model;
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

                var kind = CurrentSchemaModelBuilderHelpers.ResolveKeyConstraintKind(rows[0].ConstraintType);
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
                    IsClustered = rows[0].IsClustered,
                    IsClusteredSpecified = true,
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
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
                    Definition = CheckDefinitionNormalizer.Normalize(row.Definition),
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
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

                var referenceSchema = CurrentSchemaModelBuilderHelpers.ResolveReferenceSchema(rows[0].ReferencedSchemaName);

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
                    DeleteAction = rows[0].DeleteAction,
                    UpdateAction = rows[0].UpdateAction,
                };

                if (!string.Equals(referenceSchema, table.Schema, StringComparison.OrdinalIgnoreCase))
                {
                    constraint.UnsupportedFeature = "ForeignKeyReferenceSchema";
                }

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
        }

        private static void ApplyDefaultConstraints(
            Dictionary<int, TableModel> tableMap,
            IEnumerable<CurrentSchemaReader.DefaultRow> defaults)
        {
            if (defaults == null)
            {
                return;
            }

            foreach (var item in defaults)
            {
                if (item == null)
                    continue;

                if (string.IsNullOrWhiteSpace(item.DefaultName))
                    continue;

                if (!tableMap.TryGetValue(item.ObjectId, out var table))
                    continue;

                var constraint = new ConstraintModel
                {
                    Kind = ConstraintKind.Default,
                    Name = item.DefaultName,
                    Definition = item.DefaultDefinition,
                    DefaultColumnName = item.ColumnName,
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
            }
        }

        private static void ApplyExtendedProperties(
            Dictionary<int, TableModel> tableMap,
            Dictionary<(int ObjectId, int ColumnId), ColumnModel> columnIdMap,
            IEnumerable<CurrentSchemaReader.ExtendedPropertyRow> extendedProperties)
        {
            if (extendedProperties == null)
            {
                return;
            }

            foreach (var item in extendedProperties)
            {
                if (item == null)
                    continue;

                if (!tableMap.TryGetValue(item.MajorId, out var table))
                    continue;

                if (item.MinorId == 0)
                {
                    table.Description = item.PropertyValue;
                }
                else
                {
                    if (columnIdMap.TryGetValue((item.MajorId, item.MinorId), out var column))
                    {
                        column.Description = item.PropertyValue;
                    }
                }
            }
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

                var firstRow = rows[0];
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
                    IsClustered = firstRow.IsClustered,
                    FilterPredicate = firstRow.FilterPredicate,
                    KeyColumns = keyColumns,
                    IncludeColumns = includeColumns.Count == 0 ? null : includeColumns,
                    Options = CurrentSchemaModelBuilderHelpers.BuildIndexOptionsFromRow(firstRow),
                };

                table.Indexes[IdentifierHelper.NormalizeNameKey(index.Name)] = index;
            }
        }
    }
}
