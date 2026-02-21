using System;
using System.Linq;

namespace SqlSchemaDef.Cli
{
    internal static class CliArgumentParser
    {
        private static readonly char[] CsvSeparator = { ',' };

        internal static string[] ParseCsvArg(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(CsvSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
        }
    }
}
