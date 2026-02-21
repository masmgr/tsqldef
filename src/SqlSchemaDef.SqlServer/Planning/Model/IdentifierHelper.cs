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
            if (errors?.Count > 0)
            {
                return true;
            }

            var token = GetFirstMeaningfulToken(tokens);
            if (token == null)
            {
                return false;
            }

            return token.TokenType != TSqlTokenType.Identifier &&
                   token.TokenType != TSqlTokenType.QuotedIdentifier;
        }

        private static TSqlParserToken GetFirstMeaningfulToken(IList<TSqlParserToken> tokens)
        {
            if (tokens == null || tokens.Count == 0)
            {
                return null;
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token == null)
                {
                    continue;
                }

                if (IsIgnorableToken(token.TokenType))
                {
                    continue;
                }

                return token;
            }

            return null;
        }

        private static bool IsIgnorableToken(TSqlTokenType tokenType)
        {
            return tokenType == TSqlTokenType.WhiteSpace ||
                   tokenType == TSqlTokenType.EndOfFile;
        }
    }
}
