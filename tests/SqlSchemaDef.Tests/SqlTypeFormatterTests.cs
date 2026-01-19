using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class SqlTypeFormatterTests
{
    [Fact]
    public void Format_NVarCharMax_ReturnsMax()
    {
        var text = SqlTypeFormatter.Format("nvarchar", -1, 0, 0);

        Assert.Equal("nvarchar(max)", text);
    }

    [Fact]
    public void Format_Decimal_ReturnsPrecisionScale()
    {
        var text = SqlTypeFormatter.Format("decimal", 0, 18, 2);

        Assert.Equal("decimal(18,2)", text);
    }
}
