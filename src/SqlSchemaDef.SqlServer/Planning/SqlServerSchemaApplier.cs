using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class SqlServerSchemaApplier : ISchemaApplier
    {
        private readonly ILogger _logger;

        public SqlServerSchemaApplier(ILogger logger = null) => _logger = logger;

        public async Task ApplyAsync(
            DbConnection connection,
            MigrationPlan plan,
            ApplyOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            var sqlConnection = connection as SqlConnection;
            if (sqlConnection == null)
            {
                throw new ArgumentException("SqlServerSchemaApplier requires Microsoft.Data.SqlClient.SqlConnection.", nameof(connection));
            }

            options = options ?? new ApplyOptions();
            PlanValidator.ValidateForApply(plan);

            var operationsToApply = plan.Operations;
            if (options.ApplyProposals && plan.Proposals.Count > 0 && plan.Operations.Count > 0)
            {
                operationsToApply = FilterOperationsCoveredByProposals(plan.Operations, plan.Proposals);
            }

            var applyOperations = operationsToApply.Count > 0;
            var applyProposals = options.ApplyProposals && plan.Proposals.Count > 0;
            if (!applyOperations && !applyProposals)
            {
                return;
            }

            SqlTransaction tx = null;
            try
            {
                if (options.TransactionMode == ApplyTransactionMode.SingleTransaction)
                {
                    tx = sqlConnection.BeginTransaction();
                }

                if (applyOperations)
                {
                    await ExecuteOperationsAsync(sqlConnection, operationsToApply, tx, cancellationToken).ConfigureAwait(false);
                }

                if (applyProposals)
                {
                    await ExecuteProposalsAsync(sqlConnection, plan.Proposals, tx, cancellationToken).ConfigureAwait(false);
                }

                tx?.Commit();
            }
            catch
            {
                if (tx != null)
                {
                    try
                    {
                        tx.Rollback();
                    }
                    catch
                    {
                        // Ignore rollback failures (connection might be broken).
                    }
                }

                throw;
            }
            finally
            {
                tx?.Dispose();
            }
        }

        private async Task ExecuteOperationsAsync(
            SqlConnection sqlConnection,
            IReadOnlyList<SqlOperation> operations,
            SqlTransaction tx,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var op = operations[i];
                if (op == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(op.Sql))
                {
                    throw new InvalidOperationException("Operation.Sql is empty.");
                }

                _logger?.LogInformation("Applying: {Description}", op.Description ?? op.Kind.ToString());

                using (var cmd = sqlConnection.CreateCommand())
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = op.Sql;
                    if (tx != null)
                    {
                        cmd.Transaction = tx;
                    }

                    try
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        throw new ApplyFailedException(
                            "Failed to apply operation: " + (op.Description ?? op.Kind.ToString()),
                            op,
                            ex);
                    }
                }
            }
        }

        private async Task ExecuteProposalsAsync(
            SqlConnection sqlConnection,
            IReadOnlyList<RebuildProposal> proposals,
            SqlTransaction tx,
            CancellationToken cancellationToken)
        {
            for (int p = 0; p < proposals.Count; p++)
            {
                var proposal = proposals[p];

                if (!string.IsNullOrEmpty(proposal.Warning))
                {
                    _logger?.LogWarning("Rebuild warning for {Target}: {Warning}",
                        proposal.Target?.ToDisplayName() ?? "(unknown)", proposal.Warning);
                }

                _logger?.LogInformation("Executing rebuild: {Description}", proposal.Description ?? string.Empty);

                if (proposal.Steps == null)
                {
                    continue;
                }

                for (int s = 0; s < proposal.Steps.Count; s++)
                {
                    var step = proposal.Steps[s];

                    _logger?.LogInformation("  Step {StepKind}: {Description}", step.Kind, step.Description ?? string.Empty);

                    var fragments = RebuildStepSqlSplitter.Split(step.Sql);
                    for (int f = 0; f < fragments.Count; f++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var fragment = fragments[f];

                        using (var cmd = sqlConnection.CreateCommand())
                        {
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText = fragment;
                            if (tx != null)
                            {
                                cmd.Transaction = tx;
                            }

                            try
                            {
                                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                throw new RebuildFailedException(
                                    "Failed to execute rebuild step " + step.Kind + " for " +
                                    (proposal.Target?.ToDisplayName() ?? "(unknown)"),
                                    proposal,
                                    step,
                                    ex);
                            }
                        }
                    }
                }
            }
        }

        private IReadOnlyList<SqlOperation> FilterOperationsCoveredByProposals(
            IReadOnlyList<SqlOperation> operations,
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
                    _logger?.LogInformation(
                        "Skipping operation covered by rebuild proposal: {Description}",
                        operation?.Description ?? operation?.Kind.ToString() ?? "(unknown)");
                    continue;
                }

                filtered.Add(operation);
            }

            return filtered;
        }
    }
}
