using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class IdentifierEscapingAnalyzer
    {
        internal static bool RequiresEscaping(IList<ParseError> errors, IList<TSqlParserToken> tokens)
        {
            if (errors != null && errors.Count > 0)
            {
                return true;
            }

            var token = GetFirstMeaningfulToken(tokens);
            if (token == null)
            {
                return false;
            }

            return !IsIdentifierToken(token.TokenType);
        }

        internal static TSqlParserToken GetFirstMeaningfulToken(IList<TSqlParserToken> tokens)
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

        internal static bool IsIdentifierToken(TSqlTokenType tokenType)
        {
            return tokenType == TSqlTokenType.Identifier ||
                   tokenType == TSqlTokenType.QuotedIdentifier;
        }

        internal static bool IsIgnorableToken(TSqlTokenType tokenType)
        {
            return tokenType == TSqlTokenType.WhiteSpace ||
                   tokenType == TSqlTokenType.EndOfFile;
        }
    }
}
