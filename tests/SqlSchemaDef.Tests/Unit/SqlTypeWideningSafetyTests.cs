using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests.Unit
{
    public class SqlTypeWideningSafetyTests
    {
        // Integer family widening
        [Theory]
        [InlineData("tinyint", "smallint")]
        [InlineData("tinyint", "int")]
        [InlineData("tinyint", "bigint")]
        [InlineData("smallint", "int")]
        [InlineData("smallint", "bigint")]
        [InlineData("int", "bigint")]
        public void IsSafeTypeChange_IntegerWidening_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        [Theory]
        [InlineData("bigint", "int")]
        [InlineData("int", "smallint")]
        [InlineData("smallint", "tinyint")]
        [InlineData("bigint", "tinyint")]
        public void IsSafeTypeChange_IntegerNarrowing_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // Money family
        [Fact]
        public void IsSafeTypeChange_SmallmoneyToMoney_ReturnsTrue()
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange("smallmoney", "money"));
        }

        [Fact]
        public void IsSafeTypeChange_MoneyToSmallmoney_ReturnsFalse()
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange("money", "smallmoney"));
        }

        // Float family
        [Fact]
        public void IsSafeTypeChange_RealToFloat_ReturnsTrue()
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange("real", "float"));
        }

        [Fact]
        public void IsSafeTypeChange_FloatToReal_ReturnsFalse()
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange("float", "real"));
        }

        // String family (non-Unicode) — size widening
        [Theory]
        [InlineData("varchar(50)", "varchar(100)")]
        [InlineData("varchar(100)", "varchar(max)")]
        [InlineData("char(10)", "char(20)")]
        [InlineData("char(10)", "varchar(20)")]
        public void IsSafeTypeChange_NonUnicodeStringWidening_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        [Theory]
        [InlineData("varchar(100)", "varchar(50)")]
        [InlineData("varchar(max)", "varchar(100)")]
        [InlineData("char(20)", "char(10)")]
        public void IsSafeTypeChange_NonUnicodeStringNarrowing_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // String family (Unicode) — size widening
        [Theory]
        [InlineData("nvarchar(50)", "nvarchar(100)")]
        [InlineData("nvarchar(100)", "nvarchar(max)")]
        [InlineData("nchar(10)", "nchar(20)")]
        [InlineData("nchar(10)", "nvarchar(20)")]
        public void IsSafeTypeChange_UnicodeStringWidening_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        [Theory]
        [InlineData("nvarchar(100)", "nvarchar(50)")]
        [InlineData("nvarchar(max)", "nvarchar(100)")]
        public void IsSafeTypeChange_UnicodeStringNarrowing_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // Binary family
        [Theory]
        [InlineData("binary(10)", "binary(20)")]
        [InlineData("varbinary(100)", "varbinary(200)")]
        [InlineData("varbinary(100)", "varbinary(max)")]
        [InlineData("binary(10)", "varbinary(20)")]
        public void IsSafeTypeChange_BinaryWidening_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        [Theory]
        [InlineData("varbinary(200)", "varbinary(100)")]
        [InlineData("varbinary(max)", "varbinary(100)")]
        public void IsSafeTypeChange_BinaryNarrowing_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // DateTime family
        [Theory]
        [InlineData("date", "datetime")]
        [InlineData("date", "datetime2")]
        [InlineData("smalldatetime", "datetime")]
        [InlineData("smalldatetime", "datetime2")]
        [InlineData("datetime", "datetime2")]
        public void IsSafeTypeChange_DateTimeWidening_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        [Theory]
        [InlineData("datetime2", "date")]
        [InlineData("datetime", "date")]
        [InlineData("datetime2", "smalldatetime")]
        public void IsSafeTypeChange_DateTimeNarrowing_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // Decimal/Numeric family
        [Theory]
        [InlineData("decimal(10,2)", "decimal(18,4)")]
        [InlineData("decimal(10,2)", "decimal(10,2)")]
        [InlineData("numeric(10,2)", "numeric(18,4)")]
        [InlineData("decimal(10,2)", "numeric(18,4)")]
        public void IsSafeTypeChange_DecimalWidening_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        [Theory]
        [InlineData("decimal(18,4)", "decimal(10,2)")]
        [InlineData("decimal(18,4)", "decimal(18,2)")]
        [InlineData("decimal(18,2)", "decimal(10,2)")]
        public void IsSafeTypeChange_DecimalNarrowing_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // Cross-family (always unsafe)
        [Theory]
        [InlineData("varchar(100)", "int")]
        [InlineData("int", "varchar(100)")]
        [InlineData("datetime", "varchar(50)")]
        [InlineData("bit", "int")]
        public void IsSafeTypeChange_CrossFamily_ReturnsFalse(string current, string desired)
        {
            Assert.False(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // Same type → safe (no change)
        [Theory]
        [InlineData("int", "int")]
        [InlineData("varchar(100)", "varchar(100)")]
        [InlineData("nvarchar(max)", "nvarchar(max)")]
        [InlineData("decimal(10,2)", "decimal(10,2)")]
        public void IsSafeTypeChange_SameType_ReturnsTrue(string current, string desired)
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange(current, desired));
        }

        // Case insensitive
        [Fact]
        public void IsSafeTypeChange_CaseInsensitive_ReturnsTrue()
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange("INT", "BIGINT"));
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange("VARCHAR(50)", "VARCHAR(100)"));
        }

        // Spaces in type strings (ScriptDom variations)
        [Fact]
        public void IsSafeTypeChange_WithSpaces_ReturnsTrue()
        {
            Assert.True(SqlTypeWideningSafety.IsSafeTypeChange("decimal(10, 2)", "decimal(18, 4)"));
        }
    }
}
