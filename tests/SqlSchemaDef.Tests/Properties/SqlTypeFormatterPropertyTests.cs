using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using SqlSchemaDef.SqlServer.Planning;
using SqlSchemaDef.Tests.Properties.Generators;

namespace SqlSchemaDef.Tests.Properties;

public sealed class SqlTypeFormatterPropertyTests
{
    [Property(MaxTest = 200)]
    public Property Format_AlwaysStartsWithTypeName()
    {
        return Prop.ForAll(DomainArbitraries.SqlTypeFormatterInputArb(), input =>
        {
            var result = SqlTypeFormatter.Format(input.TypeName, input.MaxLength, input.Precision, input.Scale);
            var expected = input.TypeName.Trim().ToLowerInvariant();
            return result.StartsWith(expected, StringComparison.Ordinal)
                .Label($"type='{input.TypeName}' result='{result}'");
        });
    }

    [Property(MaxTest = 200)]
    public Property Format_ParenthesesAreBalanced()
    {
        return Prop.ForAll(DomainArbitraries.SqlTypeFormatterInputArb(), input =>
        {
            var result = SqlTypeFormatter.Format(input.TypeName, input.MaxLength, input.Precision, input.Scale);
            var open = result.Count(c => c == '(');
            var close = result.Count(c => c == ')');
            return (open == close).Label($"result='{result}' open={open} close={close}");
        });
    }

    [Property(MaxTest = 200)]
    public Property Format_AlwaysProducesNonEmptyString()
    {
        return Prop.ForAll(DomainArbitraries.SqlTypeFormatterInputArb(), input =>
        {
            var result = SqlTypeFormatter.Format(input.TypeName, input.MaxLength, input.Precision, input.Scale);
            return (!string.IsNullOrWhiteSpace(result))
                .Label($"type='{input.TypeName}' result='{result}'");
        });
    }
}
