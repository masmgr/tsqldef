using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class CheckDefinitionNormalizerTests
{
    [Theory]
    [InlineData("([Age]>(0))", "[Age] > 0")]
    [InlineData("[Age] > 0", "[Age] > 0")]
    [InlineData("([Amount]>=(0))", "[Amount] >= 0")]
    [InlineData("([Name] <> '')", "[Name] <> ''")]
    [InlineData("([Price]>(0))", "[Price] > 0")]
    public void Normalize_ReturnsCanonicalForm(string input, string expected)
    {
        var result = CheckDefinitionNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("([Age]>(0))", "[Age] > 0")]
    [InlineData("([Amount]>=(0))", "[Amount] >= 0")]
    [InlineData("([Name]<>'')", "[Name] <> ''")]
    [InlineData("[Age] > 0", "[Age] > 0")]
    public void Normalize_DbAndDesiredFormsAreEquivalent(string dbForm, string desiredNormalized)
    {
        var dbNormalized = CheckDefinitionNormalizer.Normalize(dbForm);
        Assert.Equal(desiredNormalized, dbNormalized, ignoreCase: true);
    }

    [Theory]
    [InlineData("Age > 0", "Age > 0")]
    [InlineData("(Age > 0)", "Age > 0")]
    public void Normalize_UnescapedIdentifiers_PreserveUnescapedForm(string input, string expected)
    {
        // ScriptDom preserves identifier quoting style — unescaped identifiers stay unescaped.
        // The SchemaDiffer's NormalizedCheckEquals strips brackets for comparison,
        // so [Age] and Age are treated as equivalent.
        var result = CheckDefinitionNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalize_NullReturnsNull()
    {
        Assert.Null(CheckDefinitionNormalizer.Normalize(null));
    }

    [Fact]
    public void Normalize_WhitespaceReturnsWhitespace()
    {
        Assert.Equal("  ", CheckDefinitionNormalizer.Normalize("  "));
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var input = "([Age]>(0))";
        var first = CheckDefinitionNormalizer.Normalize(input);
        var second = CheckDefinitionNormalizer.Normalize(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Normalize_DesiredFormIsIdempotent()
    {
        var input = "[Age] > 0";
        var result = CheckDefinitionNormalizer.Normalize(input);
        Assert.Equal(input, result);
    }
}
