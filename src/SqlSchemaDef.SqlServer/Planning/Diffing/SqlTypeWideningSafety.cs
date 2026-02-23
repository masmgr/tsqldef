using System;
using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class SqlTypeWideningSafety
    {
        private enum TypeFamily
        {
            Unknown,
            Integer,
            Money,
            Float,
            NonUnicodeString,
            UnicodeString,
            Binary,
            DateTime,
            Decimal,
        }

        private static readonly Dictionary<string, int> IntegerRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "tinyint", 1 },
            { "smallint", 2 },
            { "int", 3 },
            { "bigint", 4 },
        };

        private static readonly Dictionary<string, int> MoneyRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "smallmoney", 1 },
            { "money", 2 },
        };

        private static readonly Dictionary<string, int> FloatRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "real", 1 },
            { "float", 2 },
        };

        private static readonly Dictionary<string, int> DateTimeRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "date", 1 },
            { "smalldatetime", 2 },
            { "datetime", 3 },
            { "datetime2", 4 },
        };

        private static readonly HashSet<string> FixedNonUnicode = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "char" };
        private static readonly HashSet<string> VarNonUnicode = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "varchar" };
        private static readonly HashSet<string> FixedUnicode = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nchar" };
        private static readonly HashSet<string> VarUnicode = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nvarchar" };
        private static readonly HashSet<string> FixedBinary = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "binary" };
        private static readonly HashSet<string> VarBinary = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "varbinary" };
        private static readonly HashSet<string> DecimalTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "decimal", "numeric" };

        /// <summary>
        /// Returns true if changing from <paramref name="currentType"/> to <paramref name="desiredType"/>
        /// is a safe widening conversion that can be performed via ALTER TABLE ALTER COLUMN.
        /// </summary>
        public static bool IsSafeTypeChange(string currentType, string desiredType)
        {
            if (string.IsNullOrEmpty(currentType) || string.IsNullOrEmpty(desiredType))
            {
                return false;
            }

            ParseSqlType(currentType, out var currentBase, out var currentSize1, out var currentSize2);
            ParseSqlType(desiredType, out var desiredBase, out var desiredSize1, out var desiredSize2);

            var currentFamily = GetFamily(currentBase);
            var desiredFamily = GetFamily(desiredBase);

            if (currentFamily == TypeFamily.Unknown || desiredFamily == TypeFamily.Unknown)
            {
                return false;
            }

            if (currentFamily != desiredFamily)
            {
                return false;
            }

            switch (currentFamily)
            {
                case TypeFamily.Integer:
                    return IntegerRank[desiredBase] >= IntegerRank[currentBase];

                case TypeFamily.Money:
                    return MoneyRank[desiredBase] >= MoneyRank[currentBase];

                case TypeFamily.Float:
                    return FloatRank[desiredBase] >= FloatRank[currentBase];

                case TypeFamily.DateTime:
                    return DateTimeRank[desiredBase] >= DateTimeRank[currentBase];

                case TypeFamily.Decimal:
                    return desiredSize1 >= currentSize1 && desiredSize2 >= currentSize2;

                case TypeFamily.NonUnicodeString:
                    return IsSizedTypeWidening(currentBase, currentSize1, desiredBase, desiredSize1,
                        FixedNonUnicode, VarNonUnicode);

                case TypeFamily.UnicodeString:
                    return IsSizedTypeWidening(currentBase, currentSize1, desiredBase, desiredSize1,
                        FixedUnicode, VarUnicode);

                case TypeFamily.Binary:
                    return IsSizedTypeWidening(currentBase, currentSize1, desiredBase, desiredSize1,
                        FixedBinary, VarBinary);

                default:
                    return false;
            }
        }

        private static bool IsSizedTypeWidening(
            string currentBase, int currentSize,
            string desiredBase, int desiredSize,
            HashSet<string> fixedTypes, HashSet<string> varTypes)
        {
            // fixed → var is allowed if size doesn't narrow
            // var → fixed is not allowed (loss of variable-length semantics)
            var currentIsFixed = fixedTypes.Contains(currentBase);
            var desiredIsFixed = fixedTypes.Contains(desiredBase);

            if (!currentIsFixed && desiredIsFixed)
            {
                return false;
            }

            // MAX is represented as -1, treat as infinite
            if (desiredSize == -1)
            {
                // anything → MAX is safe widening (as long as family matches and not var→fixed)
                return true;
            }

            if (currentSize == -1)
            {
                // MAX → non-MAX is narrowing
                return false;
            }

            return desiredSize >= currentSize;
        }

        private static TypeFamily GetFamily(string baseName)
        {
            if (IntegerRank.ContainsKey(baseName))
                return TypeFamily.Integer;
            if (MoneyRank.ContainsKey(baseName))
                return TypeFamily.Money;
            if (FloatRank.ContainsKey(baseName))
                return TypeFamily.Float;
            if (DateTimeRank.ContainsKey(baseName))
                return TypeFamily.DateTime;
            if (DecimalTypes.Contains(baseName))
                return TypeFamily.Decimal;
            if (FixedNonUnicode.Contains(baseName) || VarNonUnicode.Contains(baseName))
                return TypeFamily.NonUnicodeString;
            if (FixedUnicode.Contains(baseName) || VarUnicode.Contains(baseName))
                return TypeFamily.UnicodeString;
            if (FixedBinary.Contains(baseName) || VarBinary.Contains(baseName))
                return TypeFamily.Binary;
            return TypeFamily.Unknown;
        }

        private static void ParseSqlType(string sqlType, out string baseName, out int size1, out int size2)
        {
            // Normalize: remove spaces
            var normalized = sqlType.Replace(" ", string.Empty);

            var parenIndex = normalized.IndexOf('(');
            if (parenIndex < 0)
            {
                baseName = normalized;
                size1 = 0;
                size2 = 0;
                return;
            }

            baseName = normalized.Substring(0, parenIndex);

            var inner = normalized.Substring(parenIndex + 1).TrimEnd(')');

            if (string.Equals(inner, "max", StringComparison.OrdinalIgnoreCase))
            {
                size1 = -1;
                size2 = 0;
                return;
            }

            var commaIndex = inner.IndexOf(',');
            if (commaIndex < 0)
            {
                size1 = int.TryParse(inner, out var s1) ? s1 : 0;
                size2 = 0;
            }
            else
            {
                size1 = int.TryParse(inner.Substring(0, commaIndex), out var s1) ? s1 : 0;
                size2 = int.TryParse(inner.Substring(commaIndex + 1), out var s2) ? s2 : 0;
            }
        }
    }
}
