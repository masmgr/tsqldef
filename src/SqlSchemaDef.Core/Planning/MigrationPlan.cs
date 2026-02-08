using System;
using System.Collections.Generic;
using System.Text;

namespace SqlSchemaDef.Core.Planning
{
    public sealed class MigrationPlan
    {
        public MigrationPlan(
            PlanMetadata metadata,
            IReadOnlyList<SqlOperation> operations,
            IReadOnlyList<SkippedItem> skipped)
        {
            Metadata = metadata;
            Operations = operations ?? throw new ArgumentNullException(nameof(operations));
            Skipped = skipped ?? throw new ArgumentNullException(nameof(skipped));
        }

        public PlanMetadata Metadata { get; }
        public IReadOnlyList<SqlOperation> Operations { get; }
        public IReadOnlyList<SkippedItem> Skipped { get; }

        public bool IsEmpty => Operations.Count == 0;

        public string ToScript(ScriptOptions options = null)
        {
            options = options ?? new ScriptOptions();
            var newLine = string.IsNullOrEmpty(options.NewLine) ? "\n" : options.NewLine;

            var sb = new StringBuilder();

            if (options.HeaderMode == ScriptHeaderMode.DryRunStyle)
            {
                sb.Append("-- SqlSchemaDef plan (v1 additive-only)").Append(newLine);
                sb.Append("-- Operations: ").Append(Operations.Count).Append(newLine);
                if (options.IncludeSkipped)
                {
                    sb.Append("-- Skipped: ").Append(Skipped.Count).Append(newLine);
                }
                sb.Append(newLine);
            }

            for (int i = 0; i < Operations.Count; i++)
            {
                var op = Operations[i];
                var sql = op?.Sql ?? string.Empty;

                sb.Append(sql);

                if (options.TerminateWithSemicolon && !EndsWithSemicolon(sql))
                {
                    sb.Append(';');
                }

                sb.Append(newLine);

                if (i + 1 < Operations.Count)
                {
                    sb.Append(newLine);
                }
            }

            if (options.IncludeSkipped && Skipped.Count > 0)
            {
                if (Operations.Count > 0)
                {
                    sb.Append(newLine);
                }

                for (int i = 0; i < Skipped.Count; i++)
                {
                    var item = Skipped[i];
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

        private static bool EndsWithSemicolon(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return false;
            }

            for (int i = sql.Length - 1; i >= 0; i--)
            {
                var ch = sql[i];
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }
                return ch == ';';
            }

            return false;
        }
    }
}
