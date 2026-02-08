using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SqlSchemaDef.Core.Planning
{
    public interface ISchemaPlanner
    {
        Task<MigrationPlan> PlanAsync(
            DbConnection connection,
            string desiredSql,
            PlannerOptions options = null,
            CancellationToken cancellationToken = default);
    }
}
