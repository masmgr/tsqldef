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

            return PlanInternalAsync(sqlConnection, desiredSql, metadata, options, cancellationToken);
        }

        private static async Task<MigrationPlan> PlanInternalAsync(
            SqlConnection connection,
            string desiredSql,
            PlanMetadata metadata,
            PlannerOptions options,
            CancellationToken cancellationToken)
        {
            var desired = new DesiredSchemaLoader().Load(desiredSql);
            var current = await new CurrentSchemaReader()
                .ReadAsync(connection, metadata.Schema, cancellationToken)
                .ConfigureAwait(false);

            var differ = new SchemaDiffer();
            return differ.Diff(current, desired, metadata, options);
        }
    }
}
