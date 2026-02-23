using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class RebuildProposalBuilder
    {
        public static IReadOnlyList<RebuildProposal> BuildProposals(
            DatabaseModel current,
            DatabaseModel desired,
            IReadOnlyList<SkippedItem> skipped)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));
            if (desired == null)
                throw new ArgumentNullException(nameof(desired));
            if (skipped == null)
                throw new ArgumentNullException(nameof(skipped));

            var rebuildSkipped = new Dictionary<string, List<SkippedItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in skipped)
            {
                if (item.Reason != SkippedReason.AlterNotSupported || item.Target == null)
                {
                    continue;
                }

                if (item.Target.Type == SqlObjectType.Column)
                {
                    AddToRebuildSkipped(rebuildSkipped, item);
                }
            }

            if (rebuildSkipped.Count == 0)
            {
                return Array.Empty<RebuildProposal>();
            }

            var proposals = new List<RebuildProposal>();

            foreach (var entry in rebuildSkipped.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                var tableKey = entry.Key;
                var rebuildSkips = entry.Value;

                if (!desired.Tables.TryGetValue(tableKey, out var desiredTable))
                {
                    continue;
                }

                if (!current.Tables.TryGetValue(tableKey, out var currentTable))
                {
                    continue;
                }

                var proposal = BuildProposalForTable(current, currentTable, desiredTable, rebuildSkips);
                proposals.Add(proposal);
            }

            return proposals;
        }

        private static RebuildProposal BuildProposalForTable(
            DatabaseModel currentDb,
            TableModel currentTable,
            TableModel desiredTable,
            List<SkippedItem> rebuildSkips)
        {
            var schema = desiredTable.Schema;
            var tableName = desiredTable.Name;
            var shadowName = "__" + tableName + "_rebuild";
            var oldName = tableName + "_old";

            var description = BuildDescription(schema, tableName, rebuildSkips);

            var steps = new List<RebuildStep>();
            var scriptParts = new List<string>();

            // Step 1: Create shadow table
            var shadowTable = new TableModel(schema, shadowName);
            foreach (var col in desiredTable.Columns)
            {
                shadowTable.Columns[col.Key] = col.Value;
            }

            var createShadowSql = SqlStatementBuilder.BuildCreateTableSql(shadowTable);
            steps.Add(new RebuildStep
            {
                Kind = RebuildStepKind.CreateShadowTable,
                Description = "Create shadow table",
                Sql = createShadowSql,
            });
            scriptParts.Add(createShadowSql);

            // Step 2: Copy data
            var copySql = BuildCopyDataSql(schema, tableName, shadowName, currentTable, desiredTable);
            steps.Add(new RebuildStep
            {
                Kind = RebuildStepKind.CopyData,
                Description = "Copy data from original",
                Sql = copySql,
            });
            scriptParts.Add(copySql);

            // Step 3: Drop constraints on original
            var dropConstraintsSql = BuildDropConstraintsSql(schema, tableName, currentTable);
            if (!string.IsNullOrEmpty(dropConstraintsSql))
            {
                steps.Add(new RebuildStep
                {
                    Kind = RebuildStepKind.DropConstraintsOnOriginal,
                    Description = "Drop constraints on original",
                    Sql = dropConstraintsSql,
                });
                scriptParts.Add(dropConstraintsSql);
            }

            // Step 4: Rename original to _old
            var renameOldSql = "EXEC sp_rename '" + IdentifierHelper.Escape(schema) + "." + IdentifierHelper.Escape(tableName) + "', '" + oldName + "'";
            steps.Add(new RebuildStep
            {
                Kind = RebuildStepKind.RenameOriginalToOld,
                Description = "Rename original to _old",
                Sql = renameOldSql,
            });
            scriptParts.Add(renameOldSql);

            // Step 5: Rename shadow to original
            var renameShadowSql = "EXEC sp_rename '" + IdentifierHelper.Escape(schema) + "." + IdentifierHelper.Escape(shadowName) + "', '" + tableName + "'";
            steps.Add(new RebuildStep
            {
                Kind = RebuildStepKind.RenameShadowToOriginal,
                Description = "Rename shadow to original",
                Sql = renameShadowSql,
            });
            scriptParts.Add(renameShadowSql);

            // Step 6: Recreate constraints
            var recreateConstraintsSql = BuildRecreateConstraintsSql(desiredTable);
            if (!string.IsNullOrEmpty(recreateConstraintsSql))
            {
                steps.Add(new RebuildStep
                {
                    Kind = RebuildStepKind.RecreateConstraints,
                    Description = "Recreate constraints",
                    Sql = recreateConstraintsSql,
                });
                scriptParts.Add(recreateConstraintsSql);
            }

            // Step 7: Recreate indexes
            var recreateIndexesSql = BuildRecreateIndexesSql(desiredTable);
            if (!string.IsNullOrEmpty(recreateIndexesSql))
            {
                steps.Add(new RebuildStep
                {
                    Kind = RebuildStepKind.RecreateIndexes,
                    Description = "Recreate indexes",
                    Sql = recreateIndexesSql,
                });
                scriptParts.Add(recreateIndexesSql);
            }

            // Step 8: Drop old table
            var dropOldSql = "DROP TABLE " + IdentifierHelper.Escape(schema) + "." + IdentifierHelper.Escape(oldName);
            steps.Add(new RebuildStep
            {
                Kind = RebuildStepKind.DropOldTable,
                Description = "Drop old table",
                Sql = dropOldSql,
            });
            scriptParts.Add(dropOldSql);

            // Check for FK references from other tables
            var warning = BuildFkWarning(currentDb, schema, tableName);

            return new RebuildProposal
            {
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Table,
                    Schema = schema,
                    Name = tableName,
                },
                Description = description,
                Warning = warning,
                Steps = steps,
                Script = string.Join("\nGO\n", scriptParts),
            };
        }

        private static string BuildCopyDataSql(
            string schema,
            string tableName,
            string shadowName,
            TableModel currentTable,
            TableModel desiredTable)
        {
            // Use intersection of current and desired columns
            var commonColumns = new List<string>();
            foreach (var desiredCol in desiredTable.Columns.Values
                .OrderBy(c => IdentifierHelper.NormalizeNameKey(c.Name), StringComparer.OrdinalIgnoreCase))
            {
                var key = IdentifierHelper.NormalizeNameKey(desiredCol.Name);
                if (currentTable.Columns.ContainsKey(key))
                {
                    commonColumns.Add(IdentifierHelper.Escape(desiredCol.Name));
                }
            }

            if (commonColumns.Count == 0)
            {
                return "-- No common columns to copy";
            }

            var columnList = string.Join(", ", commonColumns);

            // Check if any common column has IDENTITY in desired
            var hasIdentity = false;
            foreach (var desiredCol in desiredTable.Columns.Values)
            {
                var key = IdentifierHelper.NormalizeNameKey(desiredCol.Name);
                if (currentTable.Columns.ContainsKey(key) && desiredCol.IsIdentity)
                {
                    hasIdentity = true;
                    break;
                }
            }

            var sb = new StringBuilder();
            if (hasIdentity)
            {
                sb.Append("SET IDENTITY_INSERT ").Append(IdentifierHelper.Escape(schema)).Append('.').Append(IdentifierHelper.Escape(shadowName)).Append(" ON;\n");
            }

            sb.Append("INSERT INTO ").Append(IdentifierHelper.Escape(schema)).Append('.').Append(IdentifierHelper.Escape(shadowName))
              .Append(" (").Append(columnList).Append(')')
              .Append(" SELECT ").Append(columnList)
              .Append(" FROM ").Append(IdentifierHelper.Escape(schema)).Append('.').Append(IdentifierHelper.Escape(tableName));

            if (hasIdentity)
            {
                sb.Append(";\nSET IDENTITY_INSERT ").Append(IdentifierHelper.Escape(schema)).Append('.').Append(IdentifierHelper.Escape(shadowName)).Append(" OFF");
            }

            return sb.ToString();
        }

        private static string BuildDropConstraintsSql(string schema, string tableName, TableModel currentTable)
        {
            var parts = new List<string>();

            foreach (var entry in currentTable.Constraints.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                var constraint = entry.Value;
                parts.Add("ALTER TABLE " + IdentifierHelper.Escape(schema) + "." + IdentifierHelper.Escape(tableName) +
                          " DROP CONSTRAINT " + IdentifierHelper.Escape(constraint.Name));
            }

            return parts.Count > 0 ? string.Join(";\n", parts) : null;
        }

        private static string BuildRecreateConstraintsSql(TableModel desiredTable)
        {
            var parts = new List<string>();

            foreach (var entry in desiredTable.Constraints.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(SqlStatementBuilder.BuildAddConstraintSql(desiredTable, entry.Value));
            }

            return parts.Count > 0 ? string.Join(";\n", parts) : null;
        }

        private static string BuildRecreateIndexesSql(TableModel desiredTable)
        {
            var parts = new List<string>();

            foreach (var entry in desiredTable.Indexes.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(SqlStatementBuilder.BuildCreateIndexSql(desiredTable, entry.Value));
            }

            return parts.Count > 0 ? string.Join(";\n", parts) : null;
        }

        private static string BuildFkWarning(DatabaseModel currentDb, string schema, string tableName)
        {
            var references = new List<string>();

            foreach (var tableEntry in currentDb.Tables.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                var table = tableEntry.Value;
                if (string.Equals(table.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var constraintEntry in table.Constraints)
                {
                    var constraint = constraintEntry.Value;
                    if (constraint.Kind != ConstraintKind.ForeignKey)
                    {
                        continue;
                    }

                    if (string.Equals(constraint.ReferenceTable, tableName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(constraint.ReferenceSchema ?? "dbo", schema, StringComparison.OrdinalIgnoreCase))
                    {
                        references.Add(table.Schema + "." + table.Name + "." + constraint.Name);
                    }
                }
            }

            if (references.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.Append("WARNING: The following foreign keys from other tables reference " + schema + "." + tableName);
            sb.Append("\n  and must be dropped/recreated manually:");
            foreach (var reference in references)
            {
                sb.Append("\n  ").Append(reference);
            }

            return sb.ToString();
        }

        private static void AddToRebuildSkipped(Dictionary<string, List<SkippedItem>> rebuildSkipped, SkippedItem item)
        {
            var tableKey = IdentifierHelper.BuildTableKey(item.Target.Schema, item.Target.ParentName);
            if (!rebuildSkipped.TryGetValue(tableKey, out var list))
            {
                list = new List<SkippedItem>();
                rebuildSkipped[tableKey] = list;
            }

            list.Add(item);
        }

        private static string BuildDescription(string schema, string tableName, List<SkippedItem> rebuildSkips)
        {
            var parts = new List<string>();

            var columnNames = rebuildSkips
                .Where(s => s.Target.Type == SqlObjectType.Column)
                .Select(s => s.Target.Name)
                .ToList();
            if (columnNames.Count > 0)
            {
                parts.Add("column change: " + string.Join(", ", columnNames));
            }

            var pkNames = rebuildSkips
                .Where(s => s.Target.Type == SqlObjectType.Constraint)
                .Select(s => s.Target.Name)
                .ToList();
            if (pkNames.Count > 0)
            {
                parts.Add("primary key change: " + string.Join(", ", pkNames));
            }

            return "Rebuild " + schema + "." + tableName + " (" + string.Join("; ", parts) + ")";
        }
    }
}
