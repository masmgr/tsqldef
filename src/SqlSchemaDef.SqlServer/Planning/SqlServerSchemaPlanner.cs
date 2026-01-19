using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class SqlServerSchemaPlanner : ISchemaPlanner
    {
        public Task<MigrationPlan> PlanAsync(
            DbConnection connection,
            string desiredSql,
            PlannerOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (desiredSql == null) throw new ArgumentNullException(nameof(desiredSql));

            var sqlConnection = connection as SqlConnection;
            if (sqlConnection == null)
            {
                throw new ArgumentException("SqlServerSchemaPlanner requires Microsoft.Data.SqlClient.SqlConnection.", nameof(connection));
            }

            options = options ?? new PlannerOptions();

            var metadata = new PlanMetadata
            {
                Schema = options.Schema ?? "dbo",
                PlannedAt = DateTimeOffset.UtcNow,
                PlannerVersion = typeof(SqlServerSchemaPlanner).Assembly.GetName().Version?.ToString(),
                DatabaseName = sqlConnection.Database,
                ServerVersion = sqlConnection.ServerVersion,
            };

            // TODO: Implement v1 planner:
            // - Parse desired SQL with ScriptDom
            // - Read current schema via sys catalog
            // - Diff (additive-only) -> operations + skipped
            var plan = new MigrationPlan(metadata, Array.Empty<SqlOperation>(), Array.Empty<SkippedItem>());
            return Task.FromResult(plan);
        }
    }
}

