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
            IReadOnlyList<SkippedItem> skipped,
            IReadOnlyList<RebuildProposal> proposals = null)
        {
            Metadata = metadata;
            Operations = operations ?? throw new ArgumentNullException(nameof(operations));
            Skipped = skipped ?? throw new ArgumentNullException(nameof(skipped));
            Proposals = proposals ?? Array.Empty<RebuildProposal>();
        }

        public PlanMetadata Metadata { get; }
        public IReadOnlyList<SqlOperation> Operations { get; }
        public IReadOnlyList<SkippedItem> Skipped { get; }
        public IReadOnlyList<RebuildProposal> Proposals { get; }

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

            if (options.IncludeProposals && Proposals.Count > 0)
            {
                AppendProposals(sb, Proposals, newLine, options.ProposalsWillBeApplied);
            }

            return sb.ToString();
        }

        private static void AppendProposals(
            StringBuilder sb,
            IReadOnlyList<RebuildProposal> proposals,
            string newLine,
            bool proposalsWillBeApplied)
        {
            sb.Append(newLine);
            sb.Append("-- ============================================================").Append(newLine);
            sb.Append("-- PROPOSALS ");
            if (proposalsWillBeApplied)
            {
                sb.Append("(will be applied by apply --swap)");
            }
            else
            {
                sb.Append("(review only - NOT applied automatically)");
            }
            sb.Append(newLine);
            sb.Append("-- ============================================================").Append(newLine);

            for (int p = 0; p < proposals.Count; p++)
            {
                var proposal = proposals[p];
                sb.Append("--").Append(newLine);
                sb.Append("-- Proposal: ").Append(proposal.Description ?? string.Empty).Append(newLine);

                if (!string.IsNullOrEmpty(proposal.Warning))
                {
                    sb.Append("--").Append(newLine);
                    var warningLines = proposal.Warning.Split('\n');
                    for (int w = 0; w < warningLines.Length; w++)
                    {
                        var line = warningLines[w].TrimEnd('\r');
                        sb.Append("-- ").Append(line).Append(newLine);
                    }
                }

                sb.Append("--").Append(newLine);

                if (proposal.Steps != null)
                {
                    for (int s = 0; s < proposal.Steps.Count; s++)
                    {
                        var step = proposal.Steps[s];
                        sb.Append("-- Step ").Append(s + 1).Append(": ").Append(step.Description ?? string.Empty).Append(newLine);

                        if (!string.IsNullOrEmpty(step.Sql))
                        {
                            var sqlLines = step.Sql.Split('\n');
                            for (int l = 0; l < sqlLines.Length; l++)
                            {
                                var sqlLine = sqlLines[l].TrimEnd('\r');
                                sb.Append("-- ").Append(sqlLine).Append(newLine);
                            }
                        }

                        sb.Append("-- GO").Append(newLine);
                    }
                }
            }
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
