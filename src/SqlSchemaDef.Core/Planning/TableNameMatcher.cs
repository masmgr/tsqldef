using System;
using System.Collections.Generic;

namespace SqlSchemaDef.Core.Planning
{
    public static class TableNameMatcher
    {
        public static bool IsMatch(string tableName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return tableName?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
            }

            return string.Equals(tableName, pattern, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldInclude(
            string tableName,
            IReadOnlyList<string> includePatterns,
            IReadOnlyList<string> excludePatterns)
        {
            if (includePatterns?.Count > 0)
            {
                var matched = false;
                for (int i = 0; i < includePatterns.Count; i++)
                {
                    if (IsMatch(tableName, includePatterns[i]))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            if (excludePatterns?.Count > 0)
            {
                for (int i = 0; i < excludePatterns.Count; i++)
                {
                    if (IsMatch(tableName, excludePatterns[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
