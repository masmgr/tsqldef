using FsCheck;
using FsCheck.Xunit;
using SqlSchemaDef.SqlServer.Planning;
using SqlSchemaDef.Tests.Properties.Generators;

namespace SqlSchemaDef.Tests.Properties;

public sealed class EscapeIfKeywordPropertyTests
{
    [Property(MaxTest = 100)]
    public Property EscapeIfKeyword_IsIdempotent_ForIdentifiers()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), name =>
        {
            var once = IdentifierHelper.EscapeIfKeyword(name);
            var twice = IdentifierHelper.EscapeIfKeyword(once);
            return (once == twice).Label($"once='{once}' twice='{twice}'");
        });
    }

    [Property(MaxTest = 100)]
    public Property EscapeIfKeyword_IsIdempotent_ForKeywords()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenSqlKeyword()), keyword =>
        {
            var once = IdentifierHelper.EscapeIfKeyword(keyword);
            var twice = IdentifierHelper.EscapeIfKeyword(once);
            return (once == twice).Label($"keyword='{keyword}' once='{once}' twice='{twice}'");
        });
    }

    [Property(MaxTest = 100)]
    public Property EscapeIfKeyword_NonEmptyInput_ProducesNonEmptyOutput()
    {
        return Prop.ForAll(DomainArbitraries.GenSqlIdentifier().ToArbitrary(), name =>
        {
            var escaped = IdentifierHelper.EscapeIfKeyword(name);
            return (!string.IsNullOrEmpty(escaped)).Label($"input='{name}' output='{escaped}'");
        });
    }

    [Property(MaxTest = 100)]
    public Property EscapeIfKeyword_Keywords_AreBracketEscaped()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenSqlKeyword()), keyword =>
        {
            var escaped = IdentifierHelper.EscapeIfKeyword(keyword);
            var isBracketed = escaped.StartsWith('[') && escaped.EndsWith(']');
            return isBracketed.Label($"keyword='{keyword}' escaped='{escaped}'");
        });
    }
}
