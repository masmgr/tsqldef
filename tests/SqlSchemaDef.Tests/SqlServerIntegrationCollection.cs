using Xunit;

namespace SqlSchemaDef.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection
{
    public const string Name = "SqlServerIntegration";
}

