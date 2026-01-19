using System;

namespace SqlSchemaDef.SqlServer.Planning
{
    public static class SqlTypeFormatter
    {
        public static string Format(string typeName, int maxLength, byte precision, byte scale)
        {
            if (typeName == null)
                throw new ArgumentNullException(nameof(typeName));

            var name = typeName.Trim().ToLowerInvariant();
            if (name.Length == 0)
                throw new ArgumentException("Type name is required.", nameof(typeName));

            switch (name)
            {
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return name + FormatLength(maxLength);
                case "nvarchar":
                case "nchar":
                    return name + FormatLength(maxLength < 0 ? -1 : maxLength / 2);
                case "decimal":
                case "numeric":
                    return name + "(" + precision + "," + scale + ")";
                case "datetime2":
                case "datetimeoffset":
                case "time":
                    return name + "(" + scale + ")";
                default:
                    return name;
            }
        }

        private static string FormatLength(int maxLength)
        {
            if (maxLength < 0)
                return "(max)";
            if (maxLength == 0)
                return string.Empty;
            return "(" + maxLength + ")";
        }
    }
}
