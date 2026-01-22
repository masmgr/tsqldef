using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class ExportOptions
    {
        public string Schema { get; set; }
        public bool IncludeSkipped { get; set; } = true;
        public string NewLine { get; set; } = "\n";
    }

    public sealed class ExportResult
    {
        public ExportResult(string script, IReadOnlyList<SkippedItem> skipped)
        {
            Script = script ?? string.Empty;
            Skipped = skipped ?? Array.Empty<SkippedItem>();
        }

        public string Script { get; }
        public IReadOnlyList<SkippedItem> Skipped { get; }
    }

    public sealed class SqlServerSchemaExporter
    {
        public Task<ExportResult> ExportAsync(
            DbConnection connection,
            ExportOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            var sqlConnection = connection as SqlConnection;
            if (sqlConnection == null)
            {
                throw new ArgumentException("SqlServerSchemaExporter requires Microsoft.Data.SqlClient.SqlConnection.", nameof(connection));
            }

            options = options ?? new ExportOptions();
            var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema;

            return ExportInternalAsync(sqlConnection, schema, options, cancellationToken);
        }

        private static async Task<ExportResult> ExportInternalAsync(
            SqlConnection connection,
            string schema,
            ExportOptions options,
            CancellationToken cancellationToken)
        {
            var model = await new CurrentSchemaReader()
                .ReadAsync(connection, schema, cancellationToken)
                .ConfigureAwait(false);

            var skipped = new List<SkippedItem>();
            var script = BuildScript(model, options, skipped);
            return new ExportResult(script, skipped);
        }

        private static string BuildScript(DatabaseModel model, ExportOptions options, List<SkippedItem> skipped)
        {
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

            if (options.IncludeSkipped && skipped.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(newLine);
                }

                foreach (var item in skipped)
                {
                    sb.Append("-- Skipped: ").Append(item.Reason).Append(" ");
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
            sb.Append("CREATE TABLE ").Append(table.Schema).Append(".").Append(IdentifierHelper.EscapeIfKeyword(table.Name)).Append(" (");

            for (var i = 0; i < exportable.Count; i++)
            {
                var column = exportable[i];
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(IdentifierHelper.EscapeIfKeyword(column.Name))
                    .Append(" ")
                    .Append(column.SqlType);

                if (column.IsIdentity)
                {
                    sb.Append(" IDENTITY");
                }

                if (!string.IsNullOrWhiteSpace(column.DefaultExpression))
                {
                    sb.Append(" DEFAULT ").Append(column.DefaultExpression);
                }

                sb.Append(" ").Append(column.IsNullable ? "NULL" : "NOT NULL");
            }

            sb.Append(")");
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

            var unique = index.IsUnique ? "UNIQUE " : string.Empty;
            var sql = "CREATE " + unique + "INDEX " + index.Name + " ON " +
                      table.Schema + "." + IdentifierHelper.EscapeIfKeyword(table.Name) + " (" + JoinIndexColumns(index.KeyColumns) + ")";

            if (index.IncludeColumns != null && index.IncludeColumns.Count > 0)
            {
                var include = index.IncludeColumns.Select(IdentifierHelper.EscapeIfKeyword);
                sql += " INCLUDE (" + string.Join(", ", include) + ")";
            }

            return sql;
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
    }
}
