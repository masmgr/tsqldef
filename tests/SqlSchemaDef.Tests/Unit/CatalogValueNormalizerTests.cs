using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Tests;

public sealed class CatalogValueNormalizerTests
{
    [Fact]
    public void ToInt32_ConvertsSupportedIntegerTypes()
    {
        Assert.Equal(11, CatalogValueNormalizer.ToInt32((byte)11));
        Assert.Equal(12, CatalogValueNormalizer.ToInt32((short)12));
        Assert.Equal(13, CatalogValueNormalizer.ToInt32(13));
        Assert.Equal(14, CatalogValueNormalizer.ToInt32(14L));
    }

    [Fact]
    public void ToInt32_ConvertsOtherNumericRepresentationsWithInvariantCulture()
    {
        Assert.Equal(42, CatalogValueNormalizer.ToInt32("42"));
        Assert.Equal(43, CatalogValueNormalizer.ToInt32(43.0m));
    }

    [Fact]
    public void NormalizeForeignKeyAction_MapsKnownValues()
    {
        Assert.Equal("CASCADE", CatalogValueNormalizer.NormalizeForeignKeyAction("CASCADE"));
        Assert.Equal("SET NULL", CatalogValueNormalizer.NormalizeForeignKeyAction("SET_NULL"));
        Assert.Equal("SET DEFAULT", CatalogValueNormalizer.NormalizeForeignKeyAction("SET_DEFAULT"));
    }

    [Fact]
    public void NormalizeForeignKeyAction_UnknownOrNoAction_ReturnsNull()
    {
        Assert.Null(CatalogValueNormalizer.NormalizeForeignKeyAction(null));
        Assert.Null(CatalogValueNormalizer.NormalizeForeignKeyAction(string.Empty));
        Assert.Null(CatalogValueNormalizer.NormalizeForeignKeyAction("NO_ACTION"));
        Assert.Null(CatalogValueNormalizer.NormalizeForeignKeyAction("SOMETHING_ELSE"));
    }
}
