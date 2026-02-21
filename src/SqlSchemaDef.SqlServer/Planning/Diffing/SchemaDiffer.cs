using System;
using System.Collections.Generic;
using System.Linq;
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
            var descriptionOps = new List<SqlOperation>();
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
                    DiffColumns(currentTable, desiredTable, options, addColumnOps, skipped);
                }

                DiffConstraints(currentTable, desiredTable, hasCurrentTable, addConstraintOps, addForeignKeyOps, skipped);
                DiffIndexes(currentTable, desiredTable, hasCurrentTable, createIndexOps, skipped);
                DiffDescriptions(currentTable, desiredTable, hasCurrentTable, descriptionOps, skipped);

                if (hasCurrentTable)
                {
                    CollectDroppedItems(currentTable, desiredTable, skipped);
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
            operations.AddRange(descriptionOps);

            IReadOnlyList<RebuildProposal> proposals = Array.Empty<RebuildProposal>();
            if (options.EmitProposals)
            {
                proposals = RebuildProposalBuilder.BuildProposals(current, desired, skipped);
                if (proposals.Count > 0 && operations.Count > 0)
                {
                    operations = FilterOperationsCoveredByProposals(operations, proposals);
                }
            }

            return new MigrationPlan(metadata, operations, skipped, proposals);
        }

        private static List<SqlOperation> FilterOperationsCoveredByProposals(
            List<SqlOperation> operations,
            IReadOnlyList<RebuildProposal> proposals)
        {
            var proposalTargetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < proposals.Count; i++)
            {
                var target = proposals[i].Target;
                if (target == null || target.Type != SqlObjectType.Table)
                {
                    continue;
                }

                proposalTargetKeys.Add(IdentifierHelper.BuildTableKey(target.Schema, target.Name));
            }

            if (proposalTargetKeys.Count == 0)
            {
                return operations;
            }

            var filtered = new List<SqlOperation>(operations.Count);
            for (var i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                var tableKey = GetOperationTableKey(operation);
                if (tableKey != null && proposalTargetKeys.Contains(tableKey))
                {
                    continue;
                }

                filtered.Add(operation);
            }

            return filtered;
        }

        private static string GetOperationTableKey(SqlOperation operation)
        {
            var target = operation?.Target;
            if (target == null)
            {
                return null;
            }

            switch (target.Type)
            {
                case SqlObjectType.Table:
                    if (string.IsNullOrEmpty(target.Name))
                    {
                        return null;
                    }

                    return IdentifierHelper.BuildTableKey(target.Schema, target.Name);
                case SqlObjectType.Column:
                case SqlObjectType.Constraint:
                case SqlObjectType.Index:
                case SqlObjectType.ForeignKey:
                    if (string.IsNullOrEmpty(target.ParentName))
                    {
                        return null;
                    }

                    return IdentifierHelper.BuildTableKey(target.Schema, target.ParentName);
                case SqlObjectType.Description:
                    var descriptionTable = string.IsNullOrEmpty(target.ParentName)
                        ? target.Name
                        : target.ParentName;
                    if (string.IsNullOrEmpty(descriptionTable))
                    {
                        return null;
                    }

                    return IdentifierHelper.BuildTableKey(target.Schema, descriptionTable);
                default:
                    return null;
            }
        }

        private static void DiffColumns(
            TableModel currentTable,
            TableModel desiredTable,
            PlannerOptions options,
            List<SqlOperation> addColumnOps,
            List<SkippedItem> skipped)
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

        private static void DiffConstraints(
            TableModel currentTable,
            TableModel desiredTable,
            bool hasCurrentTable,
            List<SqlOperation> addConstraintOps,
            List<SqlOperation> addForeignKeyOps,
            List<SkippedItem> skipped)
        {
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

                // Default constraints are included inline in CREATE TABLE column definitions,
                // so skip separate AddConstraint for new tables.
                if (constraint.Kind == ConstraintKind.Default && !hasCurrentTable)
                {
                    continue;
                }

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
        }

        private static void DiffIndexes(
            TableModel currentTable,
            TableModel desiredTable,
            bool hasCurrentTable,
            List<SqlOperation> createIndexOps,
            List<SkippedItem> skipped)
        {
            var currentHasClusteredIndex = hasCurrentTable &&
                currentTable.Indexes.Values.Any(ix => ix.IsClustered);

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

                if (indexEntry.Value.IsClustered && currentHasClusteredIndex)
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
                        Message = "table already has a clustered index; cannot add another in v1",
                    });
                    continue;
                }

                createIndexOps.Add(CreateIndexOperation(desiredTable, indexEntry.Value));
            }
        }

        private static void DiffDescriptions(
            TableModel currentTable,
            TableModel desiredTable,
            bool hasCurrentTable,
            List<SqlOperation> descriptionOps,
            List<SkippedItem> skipped)
        {
            var currentTableDesc = hasCurrentTable ? currentTable.Description : null;
            var desiredTableDesc = desiredTable.Description;

            if (desiredTableDesc != null)
            {
                if (currentTableDesc == null)
                {
                    descriptionOps.Add(DescriptionOperation(
                        desiredTable, null, desiredTableDesc, OperationKind.AddDescription));
                }
                else if (!string.Equals(currentTableDesc, desiredTableDesc, StringComparison.Ordinal))
                {
                    descriptionOps.Add(DescriptionOperation(
                        desiredTable, null, desiredTableDesc, OperationKind.UpdateDescription));
                }
            }
            else if (currentTableDesc != null)
            {
                skipped.Add(new SkippedItem
                {
                    Reason = SkippedReason.DropNotSupported,
                    Target = new SqlObjectRef
                    {
                        Type = SqlObjectType.Description,
                        Schema = desiredTable.Schema,
                        Name = desiredTable.Name,
                    },
                    Message = "drop is not supported in v1",
                });
            }

            foreach (var columnEntry in desiredTable.Columns.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                var desiredColumn = columnEntry.Value;
                if (desiredColumn.Description == null)
                    continue;

                string currentColumnDesc = null;
                if (hasCurrentTable && currentTable.Columns.TryGetValue(columnEntry.Key, out var currentColumn))
                {
                    currentColumnDesc = currentColumn.Description;
                }

                if (currentColumnDesc == null)
                {
                    descriptionOps.Add(DescriptionOperation(
                        desiredTable, desiredColumn.Name, desiredColumn.Description, OperationKind.AddDescription));
                }
                else if (!string.Equals(currentColumnDesc, desiredColumn.Description, StringComparison.Ordinal))
                {
                    descriptionOps.Add(DescriptionOperation(
                        desiredTable, desiredColumn.Name, desiredColumn.Description, OperationKind.UpdateDescription));
                }
            }

            if (hasCurrentTable)
            {
                foreach (var currentColumnEntry in currentTable.Columns.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var currentColumn = currentColumnEntry.Value;
                    if (currentColumn.Description == null)
                        continue;

                    string desiredColumnDesc = null;
                    if (desiredTable.Columns.TryGetValue(currentColumnEntry.Key, out var desiredColumn))
                    {
                        desiredColumnDesc = desiredColumn.Description;
                    }

                    if (desiredColumnDesc == null)
                    {
                        skipped.Add(new SkippedItem
                        {
                            Reason = SkippedReason.DropNotSupported,
                            Target = new SqlObjectRef
                            {
                                Type = SqlObjectType.Description,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = currentColumn.Name,
                            },
                            Message = "drop is not supported in v1",
                        });
                    }
                }
            }
        }

        private static SqlOperation DescriptionOperation(
            TableModel table, string columnName, string description, OperationKind kind)
        {
            string sql;
            string descText;
            if (kind == OperationKind.AddDescription)
            {
                sql = SqlStatementBuilder.BuildAddDescriptionSql(table.Schema, table.Name, columnName, description);
                descText = columnName != null
                    ? "Add description " + table.Schema + "." + table.Name + "." + columnName
                    : "Add description " + table.Schema + "." + table.Name;
            }
            else
            {
                sql = SqlStatementBuilder.BuildUpdateDescriptionSql(table.Schema, table.Name, columnName, description);
                descText = columnName != null
                    ? "Update description " + table.Schema + "." + table.Name + "." + columnName
                    : "Update description " + table.Schema + "." + table.Name;
            }

            return new SqlOperation
            {
                Kind = kind,
                Description = descText,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Description,
                    Schema = table.Schema,
                    ParentName = columnName != null ? table.Name : null,
                    Name = columnName ?? table.Name,
                },
            };
        }

        private static void CollectDroppedItems(
            TableModel currentTable,
            TableModel desiredTable,
            List<SkippedItem> skipped)
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

        private static SqlOperation AddConstraintOperation(TableModel table, ConstraintModel constraint)
        {
            var sql = SqlStatementBuilder.BuildAddConstraintSql(table, constraint);
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
            var sql = SqlStatementBuilder.BuildCreateIndexSql(table, index);
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

            if (!string.Equals(current.Collation ?? string.Empty, desired.Collation ?? string.Empty, StringComparison.OrdinalIgnoreCase))
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
                    return IsForeignKeyDifferent(current, desired);
                case ConstraintKind.Default:
                    return IsDefaultConstraintDifferent(current, desired);
                default:
                    return true;
            }
        }

        private static bool IsForeignKeyDifferent(ConstraintModel current, ConstraintModel desired)
        {
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

            if (!string.Equals(current.DeleteAction ?? string.Empty, desired.DeleteAction ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(current.UpdateAction ?? string.Empty, desired.UpdateAction ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsDefaultConstraintDifferent(ConstraintModel current, ConstraintModel desired)
        {
            if (!string.Equals(current.Definition ?? string.Empty, desired.Definition ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.Equals(current.DefaultColumnName ?? string.Empty, desired.DefaultColumnName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIndexDifferent(IndexModel current, IndexModel desired)
        {
            if (current.IsUnique != desired.IsUnique)
            {
                return true;
            }

            if (current.IsClustered != desired.IsClustered)
            {
                return true;
            }

            if (!SequenceEqual(current.KeyColumns, desired.KeyColumns))
            {
                return true;
            }

            if (!SequenceEqual(current.IncludeColumns, desired.IncludeColumns))
            {
                return true;
            }

            var currentFilter = (current.FilterPredicate ?? string.Empty).Trim();
            var desiredFilter = (desired.FilterPredicate ?? string.Empty).Trim();
            if (!string.Equals(currentFilter, desiredFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return AreIndexOptionsDifferent(current.Options, desired.Options);
        }

        private static bool AreIndexOptionsDifferent(
            IDictionary<string, string> current,
            IDictionary<string, string> desired)
        {
            if (desired == null || desired.Count == 0)
            {
                return false;
            }

            var executionTimeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ONLINE",
                "SORTINTEMPDB",
                "SORT_IN_TEMPDB",
            };

            foreach (var kv in desired)
            {
                if (executionTimeKeys.Contains(kv.Key))
                {
                    continue;
                }

                string currentVal = null;
                current?.TryGetValue(kv.Key, out currentVal);
                if (!string.Equals(currentVal ?? string.Empty, kv.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
            var sql = SqlStatementBuilder.BuildCreateTableSql(table);
            return new SqlOperation
            {
                Kind = OperationKind.CreateTable,
                Description = "Create table " + table.Schema + "." + table.Name,
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
            var sql = "ALTER TABLE " + IdentifierHelper.Escape(table.Schema) + "." + IdentifierHelper.Escape(table.Name) +
                      " ADD " + SqlStatementBuilder.BuildColumnDefinitionSql(column);

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
    }
}
