using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class SqlExpressionComparer
    {
        internal static bool Equals(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
        }

        private static string Normalize(string expression)
        {
            var parser = new TSql160Parser(true);
            using (var reader = new StringReader(expression ?? string.Empty))
            {
                IList<ParseError> errors;
                var tokens = parser.GetTokenStream(reader, out errors);
                if (errors.Count > 0)
                {
                    return expression;
                }

                var result = new StringBuilder();
                foreach (var token in tokens)
                {
                    var text = token.Text ?? string.Empty;
                    var isLiteral = token.TokenType == TSqlTokenType.AsciiStringLiteral || token.TokenType == TSqlTokenType.UnicodeStringLiteral;
                    result.Append(isLiteral ? text : text.ToUpperInvariant());
                }

                return result.ToString();
            }
        }
    }
}
