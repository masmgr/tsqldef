using System;
using System.Collections.Generic;
using System.Linq;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal enum ColumnChangeKind
    {
        None,
        SafeAlter,
        UnsafeAlter,
        DefaultOnly,
    }

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
            var alterColumnOps = new List<SqlOperation>();
            var recreateConstraintOps = new List<SqlOperation>();
            var addConstraintOps = new List<SqlOperation>();
            var recreateIndexOps = new List<SqlOperation>();
            var createIndexOps = new List<SqlOperation>();
            var addForeignKeyOps = new List<SqlOperation>();
            var recreateForeignKeyOps = new List<SqlOperation>();
            var descriptionOps = new List<SqlOperation>();
            var dropDescriptionOps = new List<SqlOperation>();
            var dropForeignKeyOps = new List<SqlOperation>();
            var dropIndexOps = new List<SqlOperation>();
            var dropConstraintOps = new List<SqlOperation>();
            var dropColumnOps = new List<SqlOperation>();
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
                    DiffColumns(currentTable, desiredTable, options, addColumnOps, alterColumnOps, dropColumnOps, skipped);
                }

                DiffConstraints(currentTable, desiredTable, hasCurrentTable, addConstraintOps, addForeignKeyOps, recreateConstraintOps, recreateForeignKeyOps, skipped);
                DiffIndexes(currentTable, desiredTable, hasCurrentTable, createIndexOps, recreateIndexOps, skipped);
                DiffDescriptions(currentTable, desiredTable, hasCurrentTable, descriptionOps, dropDescriptionOps, skipped);

                if (hasCurrentTable)
                {
                    EmitDropOperations(currentTable, desiredTable, dropForeignKeyOps, dropIndexOps, dropConstraintOps);
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
            operations.AddRange(alterColumnOps);
            operations.AddRange(recreateConstraintOps);
            operations.AddRange(addConstraintOps);
            operations.AddRange(recreateIndexOps);
            operations.AddRange(createIndexOps);
            operations.AddRange(addForeignKeyOps);
            operations.AddRange(recreateForeignKeyOps);
            operations.AddRange(descriptionOps);
            operations.AddRange(dropDescriptionOps);
            operations.AddRange(dropForeignKeyOps);
            operations.AddRange(dropIndexOps);
            operations.AddRange(dropConstraintOps);
            operations.AddRange(dropColumnOps);

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
                var tableKey = IdentifierHelper.GetOperationTableKey(operation);
                if (tableKey != null && proposalTargetKeys.Contains(tableKey))
                {
                    continue;
                }

                filtered.Add(operation);
            }

            return filtered;
        }

        private static void DiffColumns(
            TableModel currentTable,
            TableModel desiredTable,
            PlannerOptions options,
            List<SqlOperation> addColumnOps,
            List<SqlOperation> alterColumnOps,
            List<SqlOperation> dropColumnOps,
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
                            Reason = SkippedReason.UnsupportedFeatureInCurrent,
                            Target = new SqlObjectRef
                            {
                                Type = SqlObjectType.Column,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = desiredColumn.Name,
                            },
                            Message = "current column has unsupported feature: " + currentColumn.UnsupportedFeature,
                        });
                        continue;
                    }

                    var changeKind = ClassifyColumnChange(currentColumn, desiredColumn);
                    switch (changeKind)
                    {
                        case ColumnChangeKind.SafeAlter:
                            alterColumnOps.Add(AlterColumnOperation(desiredTable, desiredColumn));
                            break;

                        case ColumnChangeKind.UnsafeAlter:
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
                                Message = BuildUnsafeAlterMessage(currentColumn, desiredColumn),
                            });
                            break;

                        case ColumnChangeKind.DefaultOnly:
                        case ColumnChangeKind.None:
                            break;
                    }
                    continue;
                }

                var hasDefault = !string.IsNullOrEmpty(desiredColumn.DefaultExpression);
                if (!desiredColumn.IsNullable && !hasDefault && options.NotNullColumnAddBehavior == NotNullColumnAddBehavior.Skip)
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

                dropColumnOps.Add(DropColumnOperation(desiredTable, currentColumnEntry.Value));
            }
        }

        private static void DiffConstraints(
            TableModel currentTable,
            TableModel desiredTable,
            bool hasCurrentTable,
            List<SqlOperation> addConstraintOps,
            List<SqlOperation> addForeignKeyOps,
            List<SqlOperation> recreateConstraintOps,
            List<SqlOperation> recreateForeignKeyOps,
            List<SkippedItem> skipped)
        {
            foreach (var constraintEntry in desiredTable.Constraints.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var currentConstraint = (ConstraintModel)null;
                var foundCurrent = hasCurrentTable && currentTable.Constraints.TryGetValue(constraintEntry.Key, out currentConstraint);

                // DEFAULT constraints: SQL Server may auto-generate names (DF__Table__Col__XXXX)
                // that differ from the desired name (DF_Table_Col). Fall back to column-name matching.
                if (!foundCurrent && hasCurrentTable
                    && constraintEntry.Value.Kind == ConstraintKind.Default
                    && !string.IsNullOrEmpty(constraintEntry.Value.DefaultColumnName))
                {
                    currentConstraint = FindDefaultConstraintByColumn(currentTable, constraintEntry.Value.DefaultColumnName);
                    foundCurrent = currentConstraint != null;
                }

                if (foundCurrent)
                {
                    if (!string.IsNullOrEmpty(currentConstraint.UnsupportedFeature))
                    {
                        skipped.Add(new SkippedItem
                        {
                            Reason = SkippedReason.UnsupportedFeatureInCurrent,
                            Target = new SqlObjectRef
                            {
                                Type = constraintEntry.Value.Kind == ConstraintKind.ForeignKey
                                    ? SqlObjectType.ForeignKey
                                    : SqlObjectType.Constraint,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = constraintEntry.Value.Name,
                            },
                            Message = "current constraint has unsupported feature: " + currentConstraint.UnsupportedFeature,
                        });
                        continue;
                    }

                    if (IsConstraintDifferent(currentConstraint, constraintEntry.Value))
                    {
                        var desiredConstraint = constraintEntry.Value;
                        if (desiredConstraint.Kind == ConstraintKind.PrimaryKey)
                        {
                            // PK changes require rebuild (shadow table swap)
                            skipped.Add(new SkippedItem
                            {
                                Reason = SkippedReason.AlterNotSupported,
                                Target = new SqlObjectRef
                                {
                                    Type = SqlObjectType.Constraint,
                                    Schema = desiredTable.Schema,
                                    ParentName = desiredTable.Name,
                                    Name = desiredConstraint.Name,
                                },
                                Message = "alter is not supported in v1",
                            });
                        }
                        else
                        {
                            var recreateOp = RecreateConstraintOperation(desiredTable, currentConstraint, desiredConstraint);
                            if (desiredConstraint.Kind == ConstraintKind.ForeignKey)
                            {
                                recreateForeignKeyOps.Add(recreateOp);
                            }
                            else
                            {
                                recreateConstraintOps.Add(recreateOp);
                            }
                        }
                    }
                    continue;
                }

                var constraint = constraintEntry.Value;

                // Default constraints are included inline in column definitions (both CREATE TABLE
                // and ALTER TABLE ADD), so skip separate AddConstraint when the column definition
                // already carries the DEFAULT clause.
                if (constraint.Kind == ConstraintKind.Default)
                {
                    if (!hasCurrentTable)
                    {
                        // New table: DEFAULT is in CREATE TABLE column definition.
                        continue;
                    }

                    if (!string.IsNullOrEmpty(constraint.DefaultColumnName))
                    {
                        var colKey = IdentifierHelper.NormalizeNameKey(constraint.DefaultColumnName);
                        if (!currentTable.Columns.ContainsKey(colKey))
                        {
                            // Existing table, new column: DEFAULT is in ALTER TABLE ADD column definition.
                            continue;
                        }
                    }
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
            List<SqlOperation> recreateIndexOps,
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
                            Reason = SkippedReason.UnsupportedFeatureInCurrent,
                            Target = new SqlObjectRef
                            {
                                Type = SqlObjectType.Index,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = indexEntry.Value.Name,
                            },
                            Message = "current index has unsupported feature: " + currentIndex.UnsupportedFeature,
                        });
                        continue;
                    }

                    if (IsIndexDifferent(currentIndex, indexEntry.Value))
                    {
                        recreateIndexOps.Add(RecreateIndexOperation(desiredTable, currentIndex, indexEntry.Value));
                    }
                    continue;
                }

                if (indexEntry.Value.IsClustered && currentHasClusteredIndex)
                {
                    var existingClustered = currentTable.Indexes.Values.First(ix => ix.IsClustered);
                    recreateIndexOps.Add(RecreateIndexOperation(desiredTable, existingClustered, indexEntry.Value));
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
            List<SqlOperation> dropDescriptionOps,
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
                dropDescriptionOps.Add(DropDescriptionOperation(desiredTable, null));
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
                        dropDescriptionOps.Add(DropDescriptionOperation(desiredTable, currentColumn.Name));
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

        private static void EmitDropOperations(
            TableModel currentTable,
            TableModel desiredTable,
            List<SqlOperation> dropForeignKeyOps,
            List<SqlOperation> dropIndexOps,
            List<SqlOperation> dropConstraintOps)
        {
            foreach (var currentConstraintEntry in currentTable.Constraints.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (desiredTable.Constraints.ContainsKey(currentConstraintEntry.Key))
                {
                    continue;
                }

                var constraint = currentConstraintEntry.Value;

                // DEFAULT constraints with auto-generated names (DF__Table__Col__XXXX) won't
                // match by key. Check if the desired table has a DEFAULT on the same column.
                if (constraint.Kind == ConstraintKind.Default
                    && !string.IsNullOrEmpty(constraint.DefaultColumnName)
                    && FindDefaultConstraintByColumn(desiredTable, constraint.DefaultColumnName) != null)
                {
                    continue;
                }
                var isFk = constraint.Kind == ConstraintKind.ForeignKey;
                var sql = SqlStatementBuilder.BuildDropConstraintSql(desiredTable.Schema, desiredTable.Name, constraint.Name);
                var op = new SqlOperation
                {
                    Kind = isFk ? OperationKind.DropForeignKey : OperationKind.DropConstraint,
                    Description = "Drop constraint " + desiredTable.Schema + "." + desiredTable.Name + "." + constraint.Name,
                    Sql = sql,
                    Target = new SqlObjectRef
                    {
                        Type = isFk ? SqlObjectType.ForeignKey : SqlObjectType.Constraint,
                        Schema = desiredTable.Schema,
                        ParentName = desiredTable.Name,
                        Name = constraint.Name,
                    },
                };

                if (isFk)
                {
                    dropForeignKeyOps.Add(op);
                }
                else
                {
                    dropConstraintOps.Add(op);
                }
            }

            foreach (var currentIndexEntry in currentTable.Indexes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (desiredTable.Indexes.ContainsKey(currentIndexEntry.Key))
                {
                    continue;
                }

                var index = currentIndexEntry.Value;
                var sql = SqlStatementBuilder.BuildDropIndexSql(desiredTable.Schema, desiredTable.Name, index.Name);
                dropIndexOps.Add(new SqlOperation
                {
                    Kind = OperationKind.DropIndex,
                    Description = "Drop index " + desiredTable.Schema + "." + desiredTable.Name + "." + index.Name,
                    Sql = sql,
                    Target = new SqlObjectRef
                    {
                        Type = SqlObjectType.Index,
                        Schema = desiredTable.Schema,
                        ParentName = desiredTable.Name,
                        Name = index.Name,
                    },
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

        private static SqlOperation RecreateConstraintOperation(
            TableModel table, ConstraintModel current, ConstraintModel desired)
        {
            var dropSql = SqlStatementBuilder.BuildDropConstraintSql(table.Schema, table.Name, current.Name);
            var addSql = SqlStatementBuilder.BuildAddConstraintSql(table, desired);
            var kind = desired.Kind == ConstraintKind.ForeignKey
                ? OperationKind.RecreateForeignKey
                : OperationKind.RecreateConstraint;
            return new SqlOperation
            {
                Kind = kind,
                Description = "Recreate constraint " + table.Schema + "." + table.Name + "." + desired.Name,
                Sql = dropSql + ";\n" + addSql,
                Target = new SqlObjectRef
                {
                    Type = desired.Kind == ConstraintKind.ForeignKey
                        ? SqlObjectType.ForeignKey
                        : SqlObjectType.Constraint,
                    Schema = table.Schema,
                    ParentName = table.Name,
                    Name = desired.Name,
                },
            };
        }

        private static SqlOperation RecreateIndexOperation(TableModel table, IndexModel current, IndexModel desired)
        {
            var dropSql = SqlStatementBuilder.BuildDropIndexSql(table.Schema, table.Name, current.Name);
            var createSql = SqlStatementBuilder.BuildCreateIndexSql(table, desired);
            return new SqlOperation
            {
                Kind = OperationKind.RecreateIndex,
                Description = "Recreate index " + table.Schema + "." + table.Name + "." + desired.Name,
                Sql = dropSql + ";\n" + createSql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Index,
                    Schema = table.Schema,
                    ParentName = table.Name,
                    Name = desired.Name,
                },
            };
        }

        internal static ColumnChangeKind ClassifyColumnChange(ColumnModel current, ColumnModel desired)
        {
            var typeDiffers = !string.Equals(
                NormalizeSqlType(current.SqlType),
                NormalizeSqlType(desired.SqlType),
                StringComparison.OrdinalIgnoreCase);

            var nullDiffers = current.IsNullable != desired.IsNullable;

            var identityDiffers = current.IsIdentity != desired.IsIdentity;

            var defaultDiffers = !string.Equals(
                NormalizeDefaultDefinition(current.DefaultExpression),
                NormalizeDefaultDefinition(desired.DefaultExpression),
                StringComparison.OrdinalIgnoreCase);

            var collationDiffers = !string.Equals(
                current.Collation ?? string.Empty,
                desired.Collation ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            if (!typeDiffers && !nullDiffers && !identityDiffers && !defaultDiffers && !collationDiffers)
            {
                return ColumnChangeKind.None;
            }

            if (defaultDiffers && !typeDiffers && !nullDiffers && !identityDiffers && !collationDiffers)
            {
                return ColumnChangeKind.DefaultOnly;
            }

            if (identityDiffers)
            {
                return ColumnChangeKind.UnsafeAlter;
            }

            if (typeDiffers)
            {
                if (!SqlTypeWideningSafety.IsSafeTypeChange(current.SqlType, desired.SqlType))
                {
                    return ColumnChangeKind.UnsafeAlter;
                }
            }

            // Type is safe (or same) and no identity change — nullability, collation, or default alongside safe type
            return ColumnChangeKind.SafeAlter;
        }

        private static string BuildUnsafeAlterMessage(ColumnModel current, ColumnModel desired)
        {
            if (current.IsIdentity != desired.IsIdentity)
            {
                return "IDENTITY change requires rebuild";
            }

            return "type change from " + current.SqlType + " to " + desired.SqlType + " is not safe; requires rebuild";
        }

        private static string NormalizeSqlType(string sqlType)
        {
            if (string.IsNullOrEmpty(sqlType))
            {
                return string.Empty;
            }

            // ScriptDom may add spaces after commas in type parameters
            // (e.g. "decimal(10, 2)" vs "decimal(10,2)").
            // Remove all whitespace for comparison.
            return sqlType.Replace(" ", string.Empty);
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
                    if (desired.IsClusteredSpecified && current.IsClustered != desired.IsClustered)
                    {
                        return true;
                    }

                    return !SequenceEqual(current.Columns, desired.Columns);
                case ConstraintKind.Check:
                    return !NormalizedCheckEquals(current.Definition, desired.Definition);
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
            if (!string.Equals(
                    NormalizeDefaultDefinition(current.Definition),
                    NormalizeDefaultDefinition(desired.Definition),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.Equals(current.DefaultColumnName ?? string.Empty, desired.DefaultColumnName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDefaultDefinition(string definition)
        {
            if (string.IsNullOrEmpty(definition))
            {
                return string.Empty;
            }

            // SQL Server wraps DEFAULT definitions in extra parentheses when stored in
            // sys.default_constraints.definition (e.g. DEFAULT (1) becomes ((1))).
            // Strip redundant outer parentheses to match the desired form.
            var result = definition.Trim();
            while (result.Length >= 2 && result[0] == '(' && result[result.Length - 1] == ')')
            {
                var inner = result.Substring(1, result.Length - 2);
                if (AreParenthesesBalanced(inner))
                {
                    result = inner;
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        private static bool AreParenthesesBalanced(string text)
        {
            var depth = 0;
            var inString = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '\'')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }
                }
            }

            return depth == 0;
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

        private static ConstraintModel FindDefaultConstraintByColumn(TableModel table, string columnName)
        {
            var colKey = IdentifierHelper.NormalizeNameKey(columnName);
            foreach (var entry in table.Constraints)
            {
                if (entry.Value.Kind == ConstraintKind.Default
                    && string.Equals(
                        IdentifierHelper.NormalizeNameKey(entry.Value.DefaultColumnName ?? string.Empty),
                        colKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private static bool NormalizedCheckEquals(string left, string right)
        {
            var normalizedLeft = StripBrackets(CheckDefinitionNormalizer.Normalize(left) ?? string.Empty);
            var normalizedRight = StripBrackets(CheckDefinitionNormalizer.Normalize(right) ?? string.Empty);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripBrackets(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // Remove bracket-quoting of identifiers to normalize [Age] and Age to the same form.
            // Keep bracket characters that appear inside string literals (e.g. LIKE '[A-Z]').
            var sb = new System.Text.StringBuilder(input.Length);
            for (var i = 0; i < input.Length;)
            {
                var ch = input[i];

                if (ch == '\'')
                {
                    sb.Append(ch);
                    i++;
                    while (i < input.Length)
                    {
                        var stringCh = input[i];
                        sb.Append(stringCh);
                        i++;

                        if (stringCh != '\'')
                        {
                            continue;
                        }

                        // Escaped quote inside a string literal.
                        if (i < input.Length && input[i] == '\'')
                        {
                            sb.Append(input[i]);
                            i++;
                            continue;
                        }

                        break;
                    }

                    continue;
                }

                if (ch == '[')
                {
                    var contentStart = sb.Length;
                    i++;
                    var closed = false;
                    while (i < input.Length)
                    {
                        var identifierCh = input[i];
                        if (identifierCh == ']')
                        {
                            // Handle escaped ]] (literal bracket inside identifier)
                            if (i + 1 < input.Length && input[i + 1] == ']')
                            {
                                sb.Append(']');
                                i += 2;
                                continue;
                            }

                            i++;
                            closed = true;
                            break;
                        }

                        sb.Append(identifierCh);
                        i++;
                    }

                    // Malformed input: keep the opening bracket.
                    if (!closed)
                    {
                        sb.Insert(contentStart, '[');
                    }

                    continue;
                }

                sb.Append(ch);
                i++;
            }

            return sb.ToString();
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

        private static SqlOperation AlterColumnOperation(TableModel table, ColumnModel column)
        {
            var sql = SqlStatementBuilder.BuildAlterColumnSql(table.Schema, table.Name, column);
            return new SqlOperation
            {
                Kind = OperationKind.AlterColumn,
                Description = "Alter column " + table.Schema + "." + table.Name + "." + column.Name,
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

        private static SqlOperation DropColumnOperation(TableModel table, ColumnModel column)
        {
            var sql = SqlStatementBuilder.BuildDropColumnSql(table.Schema, table.Name, column.Name);
            return new SqlOperation
            {
                Kind = OperationKind.DropColumn,
                Description = "Drop column " + table.Schema + "." + table.Name + "." + column.Name,
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

        private static SqlOperation DropDescriptionOperation(TableModel table, string columnName)
        {
            var sql = SqlStatementBuilder.BuildDropDescriptionSql(table.Schema, table.Name, columnName);
            var descText = columnName != null
                ? "Drop description " + table.Schema + "." + table.Name + "." + columnName
                : "Drop description " + table.Schema + "." + table.Name;

            return new SqlOperation
            {
                Kind = OperationKind.DropDescription,
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
    }
}
