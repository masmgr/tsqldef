using System;
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

            if (plan.Operations.Count == 0)
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

                for (int i = 0; i < plan.Operations.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var op = plan.Operations[i];
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
    }
}
