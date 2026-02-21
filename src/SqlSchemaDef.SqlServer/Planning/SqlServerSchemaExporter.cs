using System;
using System.Collections.Generic;
using System.Data.Common;
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
        public static Task<ExportResult> ExportAsync(
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
            var schema = NormalizeAndValidateSchema(options.Schema);

            return ExportInternalAsync(sqlConnection, schema, options, cancellationToken);
        }

        private static async Task<ExportResult> ExportInternalAsync(
            SqlConnection connection,
            string schema,
            ExportOptions options,
            CancellationToken cancellationToken)
        {
            var model = await CurrentSchemaReader.ReadAsync(connection, schema, cancellationToken)
                .ConfigureAwait(false);

            var skipped = new List<SkippedItem>();
            var script = SchemaExportScriptBuilder.BuildScript(model, options, skipped);
            return new ExportResult(script, skipped);
        }

        private static string NormalizeAndValidateSchema(string schema)
        {
            return string.IsNullOrWhiteSpace(schema) ? "dbo" : schema.Trim();
        }
    }
}
