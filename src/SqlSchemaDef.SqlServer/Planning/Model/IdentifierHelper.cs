using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlSchemaDef.Core.Planning;

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

        internal static string GetOperationTableKey(SqlOperation operation)
        {
            var target = operation?.Target;
            if (target == null)
            {
                return null;
            }

            switch (target.Type)
            {
                case SqlObjectType.Table:
                    if (string.IsNullOrEmpty(target.Name))
                    {
                        return null;
                    }

                    return BuildTableKey(target.Schema, target.Name);
                case SqlObjectType.Column:
                case SqlObjectType.Constraint:
                case SqlObjectType.Index:
                case SqlObjectType.ForeignKey:
                    if (string.IsNullOrEmpty(target.ParentName))
                    {
                        return null;
                    }

                    return BuildTableKey(target.Schema, target.ParentName);
                case SqlObjectType.Description:
                    var descriptionTable = string.IsNullOrEmpty(target.ParentName)
                        ? target.Name
                        : target.ParentName;
                    if (string.IsNullOrEmpty(descriptionTable))
                    {
                        return null;
                    }

                    return BuildTableKey(target.Schema, descriptionTable);
                default:
                    return null;
            }
        }

        internal static string NormalizeIndexOptionName(string name)
        {
            var raw = (name ?? string.Empty).Trim().ToUpperInvariant();
            switch (raw)
            {
                case "PADINDEX":
                    return "PAD_INDEX";
                case "IGNOREDUPKEY":
                    return "IGNORE_DUP_KEY";
                case "ALLOWROWLOCKS":
                    return "ALLOW_ROW_LOCKS";
                case "ALLOWPAGELOCKS":
                    return "ALLOW_PAGE_LOCKS";
                case "STATISTICSNORECOMPUTE":
                    return "STATISTICS_NORECOMPUTE";
                case "SORTINTEMPDB":
                    return "SORT_IN_TEMPDB";
                case "DROPEXISTING":
                    return "DROP_EXISTING";
                default:
                    return raw;
            }
        }
    }
}
