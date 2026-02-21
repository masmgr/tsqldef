using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class SchemaExportScriptBuilder
    {
        internal static string BuildScript(DatabaseModel model, ExportOptions options, List<SkippedItem> skipped)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (skipped == null)
                throw new ArgumentNullException(nameof(skipped));

            var newLine = string.IsNullOrEmpty(options.NewLine) ? "\n" : options.NewLine;
            var sb = new StringBuilder();

            var tables = model.Tables.Values
                .OrderBy(table => IdentifierHelper.NormalizeNameKey(table.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < tables.Count; i++)
            {
                var table = tables[i];
                var createTable = BuildCreateTableSql(table, skipped);
                if (string.IsNullOrEmpty(createTable))
                {
                    skipped.Add(new SkippedItem
                    {
                        Reason = SkippedReason.UnsupportedFeatureInCurrent,
                        Target = new SqlObjectRef
                        {
                            Type = SqlObjectType.Table,
                            Schema = table.Schema,
                            Name = table.Name,
                        },
                        Message = "no exportable columns",
                    });
                    continue;
                }

                sb.Append(createTable);
                if (i + 1 < tables.Count)
                {
                    sb.Append(newLine).Append(newLine);
                }
                else
                {
                    sb.Append(newLine);
                }
            }

            var indexes = tables
                .SelectMany(table => table.Indexes.Values.Select(index => (Table: table, Index: index)))
                .OrderBy(entry => IdentifierHelper.NormalizeNameKey(entry.Table.Name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => IdentifierHelper.NormalizeNameKey(entry.Index.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (indexes.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(newLine);
                }

                for (var i = 0; i < indexes.Count; i++)
                {
                    var entry = indexes[i];
                    var indexSql = BuildCreateIndexSql(entry.Table, entry.Index, skipped);
                    if (string.IsNullOrEmpty(indexSql))
                    {
                        continue;
                    }

                    sb.Append(indexSql);
                    if (i + 1 < indexes.Count)
                    {
                        sb.Append(newLine).Append(newLine);
                    }
                    else
                    {
                        sb.Append(newLine);
                    }
                }
            }

            var descriptions = BuildDescriptionStatements(tables);
            if (descriptions.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(newLine);
                }

                for (var i = 0; i < descriptions.Count; i++)
                {
                    sb.Append(descriptions[i]);
                    if (i + 1 < descriptions.Count)
                    {
                        sb.Append(newLine).Append(newLine);
                    }
                    else
                    {
                        sb.Append(newLine);
                    }
                }
            }

            if (options.IncludeSkipped && skipped.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(newLine);
                }

                foreach (var item in skipped)
                {
                    sb.Append("-- Skipped: ").Append(item.Reason).Append(' ');
                    sb.Append(item.Target != null ? item.Target.ToDisplayName() : "(unknown)");
                    if (!string.IsNullOrEmpty(item.Message))
                    {
                        sb.Append(" - ").Append(item.Message);
                    }
                    sb.Append(newLine);
                }
            }

            return sb.ToString();
        }

        private static string BuildCreateTableSql(TableModel table, List<SkippedItem> skipped)
        {
            var columns = table.Columns.Values
                .OrderBy(column => IdentifierHelper.NormalizeNameKey(column.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var exportable = new List<ColumnModel>();
            foreach (var column in columns)
            {
                if (!string.IsNullOrEmpty(column.UnsupportedFeature))
                {
                    skipped.Add(new SkippedItem
                    {
                        Reason = SkippedReason.UnsupportedFeatureInCurrent,
                        Target = new SqlObjectRef
                        {
                            Type = SqlObjectType.Column,
                            Schema = table.Schema,
                            ParentName = table.Name,
                            Name = column.Name,
                        },
                        Message = "unsupported column feature: " + column.UnsupportedFeature,
                    });
                    continue;
                }

                exportable.Add(column);
            }

            if (exportable.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("CREATE TABLE ").Append(IdentifierHelper.Escape(table.Schema)).Append('.').Append(IdentifierHelper.Escape(table.Name)).Append(" (");

            for (var i = 0; i < exportable.Count; i++)
            {
                var column = exportable[i];
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(SqlStatementBuilder.BuildColumnDefinitionSql(column));
            }

            sb.Append(')');
            return sb.ToString();
        }

        private static string BuildCreateIndexSql(TableModel table, IndexModel index, List<SkippedItem> skipped)
        {
            if (!string.IsNullOrEmpty(index.UnsupportedFeature))
            {
                skipped.Add(new SkippedItem
                {
                    Reason = SkippedReason.UnsupportedFeatureInCurrent,
                    Target = new SqlObjectRef
                    {
                        Type = SqlObjectType.Index,
                        Schema = table.Schema,
                        ParentName = table.Name,
                        Name = index.Name,
                    },
                    Message = "unsupported index feature: " + index.UnsupportedFeature,
                });
                return string.Empty;
            }

            if (index.KeyColumns == null || index.KeyColumns.Count == 0)
            {
                skipped.Add(new SkippedItem
                {
                    Reason = SkippedReason.UnsupportedFeatureInCurrent,
                    Target = new SqlObjectRef
                    {
                        Type = SqlObjectType.Index,
                        Schema = table.Schema,
                        ParentName = table.Name,
                        Name = index.Name,
                    },
                    Message = "index has no key columns",
                });
                return string.Empty;
            }

            return SqlStatementBuilder.BuildCreateIndexSql(table, index);
        }

        private static List<string> BuildDescriptionStatements(List<TableModel> tables)
        {
            var statements = new List<string>();

            foreach (var table in tables)
            {
                if (!string.IsNullOrEmpty(table.Description))
                {
                    statements.Add(SqlStatementBuilder.BuildAddDescriptionSql(
                        table.Schema, table.Name, null, table.Description));
                }

                var columns = table.Columns.Values
                    .Where(c => !string.IsNullOrEmpty(c.Description))
                    .OrderBy(c => IdentifierHelper.NormalizeNameKey(c.Name), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var column in columns)
                {
                    statements.Add(SqlStatementBuilder.BuildAddDescriptionSql(
                        table.Schema, table.Name, column.Name, column.Description));
                }
            }

            return statements;
        }
    }
}
