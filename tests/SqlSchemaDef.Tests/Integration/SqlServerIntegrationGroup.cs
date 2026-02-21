using Xunit;

namespace SqlSchemaDef.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliSerialGroup
{
    public const string Name = "CliSerial";
}
