using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SqlSchemaDef.Core.Planning
{
    public interface ISchemaApplier
    {
        Task ApplyAsync(
            DbConnection connection,
            MigrationPlan plan,
            ApplyOptions options = null,
            CancellationToken cancellationToken = default);
    }
}
