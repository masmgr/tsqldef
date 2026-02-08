using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class SchemaDiffer
    {
        public static MigrationPlan Diff(
            DatabaseModel current,
            DatabaseModel desired,
            PlanMetadata metadata,
            PlannerOptions options = null)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));
            if (desired == null)
                throw new ArgumentNullException(nameof(desired));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            options = options ?? new PlannerOptions();

            var createTableOps = new List<SqlOperation>();
            var addColumnOps = new List<SqlOperation>();
            var addConstraintOps = new List<SqlOperation>();
            var createIndexOps = new List<SqlOperation>();
            var addForeignKeyOps = new List<SqlOperation>();
            var skipped = new List<SkippedItem>();

            var includePatterns = options.IncludeTablePatterns;
            var excludePatterns = options.ExcludeTablePatterns;

            foreach (var desiredEntry in desired.Tables.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var desiredTable = desiredEntry.Value;

                if (!TableNameMatcher.ShouldInclude(desiredTable.Name, includePatterns, excludePatterns))
                {
                    continue;
                }

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
                        if (currentTable.Columns.TryGetValue(columnEntry.Key, out var currentColumn))
                        {
                            if (!string.IsNullOrEmpty(currentColumn.UnsupportedFeature))
                            {
                                skipped.Add(new SkippedItem
                                {
                                    Reason = SkippedReason.AlterNotSupported,
                                    Target = new SqlObjectRef
                                    {
                                        Type = SqlObjectType.Column,
                                        Schema = desiredTable.Schema,
                                        ParentName = desiredTable.Name,
                                        Name = desiredColumn.Name,
                                    },
                                    Message = "alter is not supported in v1",
                                });
                                continue;
                            }

                            if (IsColumnDifferent(currentColumn, desiredColumn))
                            {
                                skipped.Add(new SkippedItem
                                {
                                    Reason = SkippedReason.AlterNotSupported,
                                    Target = new SqlObjectRef
                                    {
                                        Type = SqlObjectType.Column,
                                        Schema = desiredTable.Schema,
                                        ParentName = desiredTable.Name,
                                        Name = desiredColumn.Name,
                                    },
                                    Message = "alter is not supported in v1",
                                });
                            }
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

                    foreach (var currentColumnEntry in currentTable.Columns.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (desiredTable.Columns.ContainsKey(currentColumnEntry.Key))
                        {
                            continue;
                        }

                        skipped.Add(new SkippedItem
                        {
                            Reason = SkippedReason.DropNotSupported,
                            Target = new SqlObjectRef
                            {
                                Type = SqlObjectType.Column,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = currentColumnEntry.Value.Name,
                            },
                            Message = "drop is not supported in v1",
                        });
                    }
                }

                foreach (var constraintEntry in desiredTable.Constraints.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (hasCurrentTable && currentTable.Constraints.TryGetValue(constraintEntry.Key, out var currentConstraint))
                    {
                        if (!string.IsNullOrEmpty(currentConstraint.UnsupportedFeature))
                        {
                            skipped.Add(new SkippedItem
                            {
                                Reason = SkippedReason.AlterNotSupported,
                                Target = new SqlObjectRef
                                {
                                    Type = constraintEntry.Value.Kind == ConstraintKind.ForeignKey
                                        ? SqlObjectType.ForeignKey
                                        : SqlObjectType.Constraint,
                                    Schema = desiredTable.Schema,
                                    ParentName = desiredTable.Name,
                                    Name = constraintEntry.Value.Name,
                                },
                                Message = "alter is not supported in v1",
                            });
                            continue;
                        }

                        if (IsConstraintDifferent(currentConstraint, constraintEntry.Value))
                        {
                            skipped.Add(new SkippedItem
                            {
                                Reason = SkippedReason.AlterNotSupported,
                                Target = new SqlObjectRef
                                {
                                    Type = constraintEntry.Value.Kind == ConstraintKind.ForeignKey
                                        ? SqlObjectType.ForeignKey
                                        : SqlObjectType.Constraint,
                                    Schema = desiredTable.Schema,
                                    ParentName = desiredTable.Name,
                                    Name = constraintEntry.Value.Name,
                                },
                                Message = "alter is not supported in v1",
                            });
                        }
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
                    if (hasCurrentTable && currentTable.Indexes.TryGetValue(indexEntry.Key, out var currentIndex))
                    {
                        if (!string.IsNullOrEmpty(currentIndex.UnsupportedFeature))
                        {
                            skipped.Add(new SkippedItem
                            {
                                Reason = SkippedReason.AlterNotSupported,
                                Target = new SqlObjectRef
                                {
                                    Type = SqlObjectType.Index,
                                    Schema = desiredTable.Schema,
                                    ParentName = desiredTable.Name,
                                    Name = indexEntry.Value.Name,
                                },
                                Message = "alter is not supported in v1",
                            });
                            continue;
                        }

                        if (IsIndexDifferent(currentIndex, indexEntry.Value))
                        {
                            skipped.Add(new SkippedItem
                            {
                                Reason = SkippedReason.AlterNotSupported,
                                Target = new SqlObjectRef
                                {
                                    Type = SqlObjectType.Index,
                                    Schema = desiredTable.Schema,
                                    ParentName = desiredTable.Name,
                                    Name = indexEntry.Value.Name,
                                },
                                Message = "alter is not supported in v1",
                            });
                        }
                        continue;
                    }

                    createIndexOps.Add(CreateIndexOperation(desiredTable, indexEntry.Value));
                }

                if (hasCurrentTable)
                {
                    foreach (var currentConstraintEntry in currentTable.Constraints.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (desiredTable.Constraints.ContainsKey(currentConstraintEntry.Key))
                        {
                            continue;
                        }

                        skipped.Add(new SkippedItem
                        {
                            Reason = SkippedReason.DropNotSupported,
                            Target = new SqlObjectRef
                            {
                                Type = currentConstraintEntry.Value.Kind == ConstraintKind.ForeignKey
                                    ? SqlObjectType.ForeignKey
                                    : SqlObjectType.Constraint,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = currentConstraintEntry.Value.Name,
                            },
                            Message = "drop is not supported in v1",
                        });
                    }

                    foreach (var currentIndexEntry in currentTable.Indexes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (desiredTable.Indexes.ContainsKey(currentIndexEntry.Key))
                        {
                            continue;
                        }

                        skipped.Add(new SkippedItem
                        {
                            Reason = SkippedReason.DropNotSupported,
                            Target = new SqlObjectRef
                            {
                                Type = SqlObjectType.Index,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = currentIndexEntry.Value.Name,
                            },
                            Message = "drop is not supported in v1",
                        });
                    }
                }
            }

            foreach (var currentEntry in current.Tables.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!TableNameMatcher.ShouldInclude(currentEntry.Value.Name, includePatterns, excludePatterns))
                {
                    continue;
                }

                if (desired.Tables.ContainsKey(currentEntry.Key))
                {
                    continue;
                }

                skipped.Add(new SkippedItem
                {
                    Reason = SkippedReason.DropNotSupported,
                    Target = new SqlObjectRef
                    {
                        Type = SqlObjectType.Table,
                        Schema = currentEntry.Value.Schema,
                        Name = currentEntry.Value.Name,
                    },
                    Message = "drop is not supported in v1",
                });
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
            var prefix = "ALTER TABLE " + table.Schema + "." + IdentifierHelper.EscapeIfKeyword(table.Name) +
                         " ADD CONSTRAINT " + IdentifierHelper.EscapeIfKeyword(constraint.Name) + " ";

            switch (constraint.Kind)
            {
                case ConstraintKind.PrimaryKey:
                    return prefix + "PRIMARY KEY (" + JoinColumns(constraint.Columns) + ")";
                case ConstraintKind.Unique:
                    return prefix + "UNIQUE (" + JoinColumns(constraint.Columns) + ")";
                case ConstraintKind.Check:
                    var definition = (constraint.Definition ?? string.Empty).Trim();
                    if (definition.Length == 0)
                    {
                        throw new InvalidOperationException("CHECK constraint definition is required.");
                    }

                    if (definition.StartsWith("(", StringComparison.Ordinal) && definition.EndsWith(")", StringComparison.Ordinal))
                    {
                        return prefix + "CHECK " + definition;
                    }

                    return prefix + "CHECK (" + definition + ")";
                case ConstraintKind.ForeignKey:
                    var referenceSchema = string.IsNullOrWhiteSpace(constraint.ReferenceSchema)
                        ? "dbo"
                        : constraint.ReferenceSchema;
                    return prefix + "FOREIGN KEY (" + JoinColumns(constraint.Columns) + ") REFERENCES " +
                           referenceSchema + "." + IdentifierHelper.EscapeIfKeyword(constraint.ReferenceTable) +
                           " (" + JoinColumns(constraint.ReferenceColumns) + ")";
                default:
                    throw new InvalidOperationException("Unsupported constraint kind.");
            }
        }

        private static string BuildCreateIndexSql(TableModel table, IndexModel index)
        {
            var unique = index.IsUnique ? "UNIQUE " : string.Empty;
            var sql = "CREATE " + unique + "INDEX " + IdentifierHelper.EscapeIfKeyword(index.Name) + " ON " +
                      table.Schema + "." + IdentifierHelper.EscapeIfKeyword(table.Name) +
                      " (" + JoinIndexColumns(index.KeyColumns) + ")";

            if (index.IncludeColumns != null && index.IncludeColumns.Count > 0)
            {
                sql += " INCLUDE (" + JoinColumns(index.IncludeColumns) + ")";
            }

            return sql;
        }

        private static string JoinColumns(IReadOnlyList<string> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return string.Empty;
            }

            var escaped = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i];
                escaped[i] = string.IsNullOrWhiteSpace(name) ? string.Empty : IdentifierHelper.EscapeIfKeyword(name);
            }

            return string.Join(", ", escaped);
        }

        private static string JoinIndexColumns(IReadOnlyList<IndexKeyColumn> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (column == null || string.IsNullOrWhiteSpace(column.Name))
                {
                    parts[i] = string.Empty;
                    continue;
                }

                var name = IdentifierHelper.EscapeIfKeyword(column.Name);
                parts[i] = column.IsDescending ? name + " DESC" : name;
            }

            return string.Join(", ", parts);
        }

        private static bool IsColumnDifferent(ColumnModel current, ColumnModel desired)
        {
            if (!string.Equals(current.SqlType, desired.SqlType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current.IsNullable != desired.IsNullable)
            {
                return true;
            }

            if (current.IsIdentity != desired.IsIdentity)
            {
                return true;
            }

            if (!string.Equals(current.DefaultExpression ?? string.Empty, desired.DefaultExpression ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsConstraintDifferent(ConstraintModel current, ConstraintModel desired)
        {
            if (current.Kind != desired.Kind)
            {
                return true;
            }

            switch (current.Kind)
            {
                case ConstraintKind.PrimaryKey:
                case ConstraintKind.Unique:
                    return !SequenceEqual(current.Columns, desired.Columns);
                case ConstraintKind.Check:
                    return !string.Equals(current.Definition ?? string.Empty, desired.Definition ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                case ConstraintKind.ForeignKey:
                    if (!string.Equals(current.ReferenceSchema ?? "dbo", desired.ReferenceSchema ?? "dbo", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (!string.Equals(current.ReferenceTable ?? string.Empty, desired.ReferenceTable ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (!SequenceEqual(current.Columns, desired.Columns))
                    {
                        return true;
                    }
                    if (!SequenceEqual(current.ReferenceColumns, desired.ReferenceColumns))
                    {
                        return true;
                    }
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsIndexDifferent(IndexModel current, IndexModel desired)
        {
            if (current.IsUnique != desired.IsUnique)
            {
                return true;
            }

            if (!SequenceEqual(current.KeyColumns, desired.KeyColumns))
            {
                return true;
            }

            return !SequenceEqual(current.IncludeColumns, desired.IncludeColumns);
        }

        private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SequenceEqual(IReadOnlyList<IndexKeyColumn> left, IReadOnlyList<IndexKeyColumn> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                var leftItem = left[i];
                var rightItem = right[i];
                if (leftItem == null || rightItem == null)
                {
                    return false;
                }

                if (!string.Equals(leftItem.Name, rightItem.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (leftItem.IsDescending != rightItem.IsDescending)
                {
                    return false;
                }
            }

            return true;
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
            var sql = "ALTER TABLE " + table.Schema + "." + IdentifierHelper.EscapeIfKeyword(table.Name) +
                      " ADD " + BuildColumnDefinitionSql(column);

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
            sb.Append("CREATE TABLE ").Append(table.Schema).Append('.').Append(IdentifierHelper.EscapeIfKeyword(table.Name)).Append(" (");

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

                sb.Append(BuildColumnDefinitionSql(column));
            }

            sb.Append(')');
            return sb.ToString();
        }

        private static string BuildColumnDefinitionSql(ColumnModel column)
        {
            if (column == null)
            {
                throw new ArgumentNullException(nameof(column));
            }

            var sb = new StringBuilder();
            sb.Append(IdentifierHelper.EscapeIfKeyword(column.Name))
                .Append(' ')
                .Append(column.SqlType);

            if (column.IsIdentity)
            {
                sb.Append(" IDENTITY");
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultExpression))
            {
                sb.Append(" DEFAULT ").Append(column.DefaultExpression.Trim());
            }

            sb.Append(' ')
                .Append(column.IsNullable ? "NULL" : "NOT NULL");

            return sb.ToString();
        }
    }
}
