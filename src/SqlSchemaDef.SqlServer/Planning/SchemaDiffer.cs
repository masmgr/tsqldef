using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class SchemaDiffer
    {
        public MigrationPlan Diff(
            DatabaseModel current,
            DatabaseModel desired,
            PlanMetadata metadata,
            PlannerOptions options = null)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (desired == null) throw new ArgumentNullException(nameof(desired));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            options = options ?? new PlannerOptions();

            var createTableOps = new List<SqlOperation>();
            var addColumnOps = new List<SqlOperation>();
            var addConstraintOps = new List<SqlOperation>();
            var createIndexOps = new List<SqlOperation>();
            var addForeignKeyOps = new List<SqlOperation>();
            var skipped = new List<SkippedItem>();

            foreach (var desiredEntry in desired.Tables.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var desiredTable = desiredEntry.Value;
                var tableKey = IdentifierHelper.BuildTableKey(desiredTable.Schema, desiredTable.Name);

                var hasCurrentTable = current.Tables.TryGetValue(tableKey, out var currentTable);
                if (!hasCurrentTable)
                {
                    createTableOps.Add(CreateTableOperation(desiredTable));
                }

                if (hasCurrentTable)
                {
                    foreach (var columnEntry in desiredTable.Columns.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        var desiredColumn = columnEntry.Value;
                        if (currentTable.Columns.ContainsKey(columnEntry.Key))
                        {
                            continue;
                        }

                        if (!desiredColumn.IsNullable && options.NotNullColumnAddBehavior == NotNullColumnAddBehavior.Skip)
                        {
                            skipped.Add(new SkippedItem
                            {
                                Reason = SkippedReason.NotNullAddNotSupported,
                                Target = new SqlObjectRef
                                {
                                    Type = SqlObjectType.Column,
                                    Schema = desiredTable.Schema,
                                    ParentName = desiredTable.Name,
                                    Name = desiredColumn.Name,
                                },
                                Message = "NOT NULL column add is not supported in v1",
                            });
                            continue;
                        }

                        addColumnOps.Add(AddColumnOperation(desiredTable, desiredColumn));
                    }
                }

                foreach (var constraintEntry in desiredTable.Constraints.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (hasCurrentTable && currentTable.Constraints.ContainsKey(constraintEntry.Key))
                    {
                        continue;
                    }

                    var constraint = constraintEntry.Value;
                    var operation = AddConstraintOperation(desiredTable, constraint);
                    if (operation.Kind == OperationKind.AddForeignKey)
                    {
                        addForeignKeyOps.Add(operation);
                    }
                    else
                    {
                        addConstraintOps.Add(operation);
                    }
                }

                foreach (var indexEntry in desiredTable.Indexes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (hasCurrentTable && currentTable.Indexes.ContainsKey(indexEntry.Key))
                    {
                        continue;
                    }

                    createIndexOps.Add(CreateIndexOperation(desiredTable, indexEntry.Value));
                }
            }

            var operations = new List<SqlOperation>();
            operations.AddRange(createTableOps);
            operations.AddRange(addColumnOps);
            operations.AddRange(addConstraintOps);
            operations.AddRange(createIndexOps);
            operations.AddRange(addForeignKeyOps);

            return new MigrationPlan(metadata, operations, skipped);
        }

        private static SqlOperation AddConstraintOperation(TableModel table, ConstraintModel constraint)
        {
            var sql = BuildAddConstraintSql(table, constraint);
            var kind = constraint.Kind == ConstraintKind.ForeignKey
                ? OperationKind.AddForeignKey
                : OperationKind.AddConstraint;

            return new SqlOperation
            {
                Kind = kind,
                Description = "Add constraint " + table.Schema + "." + table.Name + "." + constraint.Name,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = constraint.Kind == ConstraintKind.ForeignKey ? SqlObjectType.ForeignKey : SqlObjectType.Constraint,
                    Schema = table.Schema,
                    ParentName = table.Name,
                    Name = constraint.Name,
                },
            };
        }

        private static SqlOperation CreateIndexOperation(TableModel table, IndexModel index)
        {
            var sql = BuildCreateIndexSql(table, index);
            return new SqlOperation
            {
                Kind = OperationKind.CreateIndex,
                Description = "Create index " + table.Schema + "." + table.Name + "." + index.Name,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Index,
                    Schema = table.Schema,
                    ParentName = table.Name,
                    Name = index.Name,
                },
            };
        }

        private static string BuildAddConstraintSql(TableModel table, ConstraintModel constraint)
        {
            var prefix = "ALTER TABLE " + table.Schema + "." + table.Name + " ADD CONSTRAINT " + constraint.Name + " ";

            switch (constraint.Kind)
            {
                case ConstraintKind.PrimaryKey:
                    return prefix + "PRIMARY KEY (" + JoinColumns(constraint.Columns) + ")";
                case ConstraintKind.Unique:
                    return prefix + "UNIQUE (" + JoinColumns(constraint.Columns) + ")";
                case ConstraintKind.Check:
                    return prefix + "CHECK " + (constraint.Definition ?? string.Empty);
                case ConstraintKind.ForeignKey:
                    var referenceSchema = string.IsNullOrWhiteSpace(constraint.ReferenceSchema)
                        ? "dbo"
                        : constraint.ReferenceSchema;
                    return prefix + "FOREIGN KEY (" + JoinColumns(constraint.Columns) + ") REFERENCES " +
                           referenceSchema + "." + constraint.ReferenceTable + " (" + JoinColumns(constraint.ReferenceColumns) + ")";
                default:
                    throw new InvalidOperationException("Unsupported constraint kind.");
            }
        }

        private static string BuildCreateIndexSql(TableModel table, IndexModel index)
        {
            var unique = index.IsUnique ? "UNIQUE " : string.Empty;
            return "CREATE " + unique + "INDEX " + index.Name + " ON " +
                   table.Schema + "." + table.Name + " (" + JoinColumns(index.KeyColumns) + ")";
        }

        private static string JoinColumns(IReadOnlyList<string> columns)
        {
            return columns == null || columns.Count == 0 ? string.Empty : string.Join(", ", columns);
        }

        private static SqlOperation CreateTableOperation(TableModel table)
        {
            var sql = BuildCreateTableSql(table);
            return new SqlOperation
            {
                Kind = OperationKind.CreateTable,
                Description = "Create table dbo." + table.Name,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Table,
                    Schema = table.Schema,
                    Name = table.Name,
                },
            };
        }

        private static SqlOperation AddColumnOperation(TableModel table, ColumnModel column)
        {
            var sql = "ALTER TABLE " + table.Schema + "." + table.Name +
                      " ADD " + column.Name + " " + column.SqlType + " " +
                      (column.IsNullable ? "NULL" : "NOT NULL");

            return new SqlOperation
            {
                Kind = OperationKind.AddColumn,
                Description = "Add column " + table.Schema + "." + table.Name + "." + column.Name,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Column,
                    Schema = table.Schema,
                    ParentName = table.Name,
                    Name = column.Name,
                },
            };
        }

        private static string BuildCreateTableSql(TableModel table)
        {
            var sb = new StringBuilder();
            sb.Append("CREATE TABLE ").Append(table.Schema).Append(".").Append(table.Name).Append(" (");

            var columns = table.Columns.Values
                .OrderBy(column => IdentifierHelper.NormalizeNameKey(column.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(column.Name)
                    .Append(" ")
                    .Append(column.SqlType)
                    .Append(" ")
                    .Append(column.IsNullable ? "NULL" : "NOT NULL");
            }

            sb.Append(")");
            return sb.ToString();
        }
    }
}
