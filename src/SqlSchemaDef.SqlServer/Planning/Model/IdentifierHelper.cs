using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class IdentifierHelper
    {
        private static readonly ConcurrentDictionary<string, bool> KeywordCache =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static string NormalizeNameKey(string name)
            => string.IsNullOrEmpty(name) ? string.Empty : name.ToUpperInvariant();

        public static string BuildTableKey(string schema, string name)
            => NormalizeNameKey(schema) + "." + NormalizeNameKey(name);

        public static string Escape(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name ?? string.Empty;
            }

            return "[" + name.Replace("]", "]]") + "]";
        }

        public static string EscapeIfKeyword(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name ?? string.Empty;
            }

            if (!RequiresEscaping(name))
            {
                return name;
            }

            return "[" + name.Replace("]", "]]") + "]";
        }

        private static bool RequiresEscaping(string name)
            => KeywordCache.GetOrAdd(name, RequiresEscapingCore);

        private static bool RequiresEscapingCore(string name)
        {
            IList<ParseError> errors;
            var parser = new TSql160Parser(true);
            var tokens = parser.GetTokenStream(new StringReader(name), out errors);
            return IdentifierEscapingAnalyzer.RequiresEscaping(errors, tokens);
        }
    }
}
