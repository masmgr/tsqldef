using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class IdentifierEscapingAnalyzerTests
{
    [Fact]
    public void RequiresEscaping_WhenErrorsExist_ReturnsTrue()
    {
        var errors = new List<ParseError> { null! };
        var result = IdentifierEscapingAnalyzer.RequiresEscaping(errors, null);

        Assert.True(result);
    }

    [Fact]
    public void RequiresEscaping_WhenIdentifierToken_ReturnsFalse()
    {
        var (tokens, errors) = GetTokens("abc");
        var result = IdentifierEscapingAnalyzer.RequiresEscaping(errors, tokens);

        Assert.False(result);
    }

    [Fact]
    public void RequiresEscaping_WhenKeywordToken_ReturnsTrue()
    {
        var (tokens, errors) = GetTokens("SELECT");
        var result = IdentifierEscapingAnalyzer.RequiresEscaping(errors, tokens);

        Assert.True(result);
    }

    [Fact]
    public void GetFirstMeaningfulToken_SkipsWhitespaceAndNullTokens()
    {
        var (tokens, _) = GetTokens("  Name");
        tokens.Insert(0, null!);

        var token = IdentifierEscapingAnalyzer.GetFirstMeaningfulToken(tokens);

        Assert.NotNull(token);
        Assert.Equal(TSqlTokenType.Identifier, token.TokenType);
    }

    [Fact]
    public void IsIgnorableToken_MatchesWhitespaceAndEofOnly()
    {
        Assert.True(IdentifierEscapingAnalyzer.IsIgnorableToken(TSqlTokenType.WhiteSpace));
        Assert.True(IdentifierEscapingAnalyzer.IsIgnorableToken(TSqlTokenType.EndOfFile));
        Assert.False(IdentifierEscapingAnalyzer.IsIgnorableToken(TSqlTokenType.Identifier));
    }

    [Fact]
    public void IsIdentifierToken_MatchesIdentifierAndQuotedIdentifier()
    {
        Assert.True(IdentifierEscapingAnalyzer.IsIdentifierToken(TSqlTokenType.Identifier));
        Assert.True(IdentifierEscapingAnalyzer.IsIdentifierToken(TSqlTokenType.QuotedIdentifier));
        Assert.False(IdentifierEscapingAnalyzer.IsIdentifierToken(TSqlTokenType.Select));
    }

    private static (IList<TSqlParserToken> Tokens, IList<ParseError> Errors) GetTokens(string input)
    {
        var parser = new TSql160Parser(true);
        IList<ParseError> errors;
        var tokens = parser.GetTokenStream(new StringReader(input), out errors);
        return (tokens, errors);
    }
}
