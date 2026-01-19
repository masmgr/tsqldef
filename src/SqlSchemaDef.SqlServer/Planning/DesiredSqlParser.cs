using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class DesiredSqlParser
    {
        public IReadOnlyList<TSqlFragment> ParseBatches(IEnumerable<SqlBatch> batches)
        {
            if (batches == null)
                throw new ArgumentNullException(nameof(batches));

            var fragments = new List<TSqlFragment>();
            var diagnostics = new List<SqlDiagnostic>();
            var parser = new TSql160Parser(true);

            foreach (var batch in batches)
            {
                if (batch == null)
                    throw new ArgumentNullException(nameof(batches), "Batch cannot be null.");

                IList<ParseError> errors;
                TSqlFragment fragment;
                using (var reader = new StringReader(batch.Text))
                {
                    fragment = parser.Parse(reader, out errors);
                }

                if (errors != null && errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        diagnostics.Add(new SqlDiagnostic
                        {
                            BatchIndex = batch.BatchIndex,
                            Line = batch.StartLine + error.Line - 1,
                            Column = error.Column,
                            Message = error.Message,
                        });
                    }
                }

                fragments.Add(fragment);
            }

            if (diagnostics.Count > 0)
            {
                throw new DesiredSqlParseException(BuildMessage(diagnostics), diagnostics);
            }

            return fragments;
        }

        private static string BuildMessage(IReadOnlyList<SqlDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            builder.Append("Failed to parse desired SQL.");

            foreach (var diagnostic in diagnostics)
            {
                builder.AppendLine();
                builder.Append(
                    $"Batch {diagnostic.BatchIndex}, line {diagnostic.Line}, column {diagnostic.Column}: {diagnostic.Message}");
            }

            return builder.ToString();
        }
    }
}
