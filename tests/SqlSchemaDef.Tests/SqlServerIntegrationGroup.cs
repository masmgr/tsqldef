using Xunit;

namespace SqlSchemaDef.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerIntegrationGroup
{
    public const string Name = "SqlServerIntegration";
}
