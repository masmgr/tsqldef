using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class DatabaseModel
    {
        public DatabaseModel()
        {
            Tables = new Dictionary<string, TableModel>(StringComparer.OrdinalIgnoreCase);
        }

        public IDictionary<string, TableModel> Tables { get; }

        public TableModel GetOrAddTable(string schema, string name)
        {
            if (string.IsNullOrWhiteSpace(schema))
                throw new ArgumentException("Schema is required.", nameof(schema));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Table name is required.", nameof(name));

            var key = IdentifierHelper.BuildTableKey(schema, name);
            if (!Tables.TryGetValue(key, out var table))
            {
                table = new TableModel(schema, name);
                Tables[key] = table;
            }

            return table;
        }
    }

    public sealed class TableModel
    {
        public TableModel(string schema, string name)
        {
            Schema = schema;
            Name = name;
            Columns = new Dictionary<string, ColumnModel>(StringComparer.OrdinalIgnoreCase);
            Constraints = new Dictionary<string, ConstraintModel>(StringComparer.OrdinalIgnoreCase);
            Indexes = new Dictionary<string, IndexModel>(StringComparer.OrdinalIgnoreCase);
        }

        public string Schema { get; }
        public string Name { get; }
        public IDictionary<string, ColumnModel> Columns { get; }
        public IDictionary<string, ConstraintModel> Constraints { get; }
        public IDictionary<string, IndexModel> Indexes { get; }
        public string Description { get; set; }
    }

    public sealed class ColumnModel
    {
        public string Name { get; set; }
        public string SqlType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public string DefaultExpression { get; set; }
        public bool IsFromAlterAdd { get; set; }
        public string UnsupportedFeature { get; set; }
        public string Description { get; set; }
    }

    public sealed class ConstraintModel
    {
        public ConstraintKind Kind { get; set; }
        public string Name { get; set; }
        public IReadOnlyList<string> Columns { get; set; }
        public string Definition { get; set; }
        public string ReferenceSchema { get; set; }
        public string ReferenceTable { get; set; }
        public IReadOnlyList<string> ReferenceColumns { get; set; }
        public string DeleteAction { get; set; }
        public string UpdateAction { get; set; }
        public string DefaultColumnName { get; set; }
        public string UnsupportedFeature { get; set; }
    }

    public enum ConstraintKind
    {
        PrimaryKey = 1,
        Unique = 2,
        Check = 3,
        ForeignKey = 4,
        Default = 5,
    }

    public sealed class IndexModel
    {
        public string Name { get; set; }
        public bool IsUnique { get; set; }
        public IReadOnlyList<IndexKeyColumn> KeyColumns { get; set; }
        public IReadOnlyList<string> IncludeColumns { get; set; }
        public string UnsupportedFeature { get; set; }
    }

    public sealed class IndexKeyColumn
    {
        public string Name { get; set; }
        public bool IsDescending { get; set; }
    }

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
