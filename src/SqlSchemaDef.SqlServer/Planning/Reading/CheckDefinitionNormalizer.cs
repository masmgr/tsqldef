using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlSchemaDef.SqlServer.Planning
{
    /// <summary>
    /// Normalizes CHECK constraint definitions by parsing with ScriptDom and regenerating.
    /// SQL Server stores CHECK definitions in its own format (e.g. "([Age]>(0))")
    /// while ScriptDom generates a different format (e.g. "[Age] > 0").
    /// This class ensures both sides use the same canonical form.
    /// </summary>
    internal static class CheckDefinitionNormalizer
    {
        private static readonly TSql160Parser Parser = new TSql160Parser(true);
        private static readonly SqlScriptGenerator Generator = new Sql160ScriptGenerator();

        // Matches parenthesised numeric literals like (0), (42), (3.14), (-1).
        // SQL Server wraps bare literals in parentheses when storing CHECK definitions,
        // but ScriptDom does not add them when parsing desired SQL.
        private static readonly Regex ParenthesisedLiteralRegex =
            new Regex(@"\((-?\d+(?:\.\d+)?)\)", RegexOptions.Compiled);

        internal static string Normalize(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
            {
                return definition;
            }

            // Try parsing the definition as-is first, then with wrapping parentheses.
            // CHECK constraint syntax requires parentheses: ALTER TABLE T ADD CHECK (<expr>).
            // DB-stored definitions always have outer parens (e.g. "([Age]>(0))"), but
            // ScriptDom's GenerateScript strips them (returning "Age > 0" without brackets).
            var result = TryNormalize("ALTER TABLE T ADD CHECK " + definition);
            if (result != null)
            {
                return result;
            }

            // Definition may lack outer parentheses (e.g. "Age > 0" from ScriptDom output).
            result = TryNormalize("ALTER TABLE T ADD CHECK (" + definition + ")");
            if (result != null)
            {
                return result;
            }

            return definition;
        }

        private static string TryNormalize(string sql)
        {
            IList<ParseError> errors;
            TSqlFragment fragment;

            using (var reader = new StringReader(sql))
            {
                fragment = Parser.Parse(reader, out errors);
            }

            if (errors != null && errors.Count > 0)
            {
                return null;
            }

            if (fragment is TSqlScript script)
            {
                foreach (var batch in script.Batches)
                {
                    foreach (var statement in batch.Statements)
                    {
                        if (statement is AlterTableAddTableElementStatement alter)
                        {
                            foreach (var constraint in alter.Definition.TableConstraints)
                            {
                                if (constraint is CheckConstraintDefinition check)
                                {
                                    Generator.GenerateScript(check.CheckCondition, out var text);
                                    var normalized = text.Trim();

                                    // Strip redundant parentheses around numeric literals.
                                    normalized = ParenthesisedLiteralRegex.Replace(normalized, "$1");

                                    return normalized;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
