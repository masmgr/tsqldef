using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class SqlBatch
    {
        public SqlBatch(int batchIndex, int startLine, string text)
        {
            BatchIndex = batchIndex;
            StartLine = startLine;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public int BatchIndex { get; }
        public int StartLine { get; }
        public string Text { get; }
    }

    public static class BatchSplitter
    {
        public static IReadOnlyList<SqlBatch> Split(string sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));

            var batches = new List<SqlBatch>();
            var current = new StringBuilder();
            var currentStartLine = 1;
            var lineNumber = 0;
            var inBlockComment = false;

            using (var reader = new StringReader(sql))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;

                    if (!inBlockComment && TryParseGoLine(line, out var repeatCount))
                    {
                        if (repeatCount.HasValue)
                        {
                            throw CreateUnsupportedSeparatorException(repeatCount.Value, lineNumber);
                        }

                        AddBatchIfNotEmpty(batches, current, currentStartLine);
                        currentStartLine = lineNumber + 1;
                        current.Clear();
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        current.Append('\n');
                    }

                    current.Append(line);
                    inBlockComment = UpdateBlockCommentState(inBlockComment, line);
                }
            }

            AddBatchIfNotEmpty(batches, current, currentStartLine);
            return batches;
        }

        private static void AddBatchIfNotEmpty(
            IList<SqlBatch> batches,
            StringBuilder current,
            int startLine)
        {
            if (current.Length == 0) return;

            var text = current.ToString();
            if (string.IsNullOrWhiteSpace(text)) return;

            batches.Add(new SqlBatch(batches.Count, startLine, text));
        }

        private static bool TryParseGoLine(string line, out int? repeatCount)
        {
            repeatCount = null;
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (trimmed.Length == 2)
            {
                return true;
            }

            if (!char.IsWhiteSpace(trimmed[2]))
            {
                return false;
            }

            var rest = trimmed.Substring(2).Trim();
            if (rest.Length == 0)
            {
                return true;
            }

            if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                repeatCount = count;
                return true;
            }

            return false;
        }

        private static UnsupportedBatchSeparatorException CreateUnsupportedSeparatorException(int count, int lineNumber)
        {
            var message =
                "Unsupported batch separator in v1." + Environment.NewLine +
                $"\"GO {count}\" is not supported; use plain \"GO\"." + Environment.NewLine +
                $"Location: line {lineNumber}.";

            return new UnsupportedBatchSeparatorException(message)
            {
                Line = lineNumber,
                SeparatorText = $"GO {count}",
            };
        }

        private static bool UpdateBlockCommentState(bool inBlockComment, string line)
        {
            var i = 0;
            while (i < line.Length)
            {
                if (!inBlockComment)
                {
                    if (i + 1 < line.Length && line[i] == '-' && line[i + 1] == '-')
                    {
                        break;
                    }

                    if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                    {
                        inBlockComment = true;
                        i += 2;
                        continue;
                    }

                    i++;
                }
                else
                {
                    if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '/')
                    {
                        inBlockComment = false;
                        i += 2;
                        continue;
                    }

                    i++;
                }
            }

            return inBlockComment;
        }
    }
}
