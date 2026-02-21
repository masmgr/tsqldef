using System;
using System.Globalization;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class CatalogValueNormalizer
    {
        internal static int ToInt32(object value)
        {
            if (value is int intValue)
                return intValue;
            if (value is short shortValue)
                return shortValue;
            if (value is byte byteValue)
                return byteValue;
            if (value is long longValue)
                return checked((int)longValue);

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        internal static string NormalizeForeignKeyAction(string actionDesc)
        {
            if (string.IsNullOrWhiteSpace(actionDesc) ||
                string.Equals(actionDesc, "NO_ACTION", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(actionDesc, "CASCADE", StringComparison.OrdinalIgnoreCase))
            {
                return "CASCADE";
            }

            if (string.Equals(actionDesc, "SET_NULL", StringComparison.OrdinalIgnoreCase))
            {
                return "SET NULL";
            }

            if (string.Equals(actionDesc, "SET_DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                return "SET DEFAULT";
            }

            return null;
        }
    }
}
