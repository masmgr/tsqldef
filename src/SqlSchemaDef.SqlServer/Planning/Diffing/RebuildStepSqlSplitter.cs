using System;
using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class RebuildStepSqlSplitter
    {
        private static readonly string[] Separators = { ";\r\n", ";\n" };

        public static IReadOnlyList<string> Split(string sql)
        {
            if (string.IsNullOrEmpty(sql))
            {
                return Array.Empty<string>();
            }

            var parts = sql.Split(Separators, StringSplitOptions.None);
            var result = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }
    }
}
