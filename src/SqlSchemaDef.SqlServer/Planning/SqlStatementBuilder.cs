using System;
using System.Collections.Generic;
using System.Text;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class SqlStatementBuilder
    {
        internal static string BuildCreateTableSql(TableModel table)
        {
            var sb = new StringBuilder();
            sb.Append("CREATE TABLE ").Append(table.Schema).Append('.').Append(IdentifierHelper.EscapeIfKeyword(table.Name)).Append(" (");

            var columns = new List<ColumnModel>(table.Columns.Values);
            columns.Sort((a, b) => string.Compare(
                IdentifierHelper.NormalizeNameKey(a.Name),
                IdentifierHelper.NormalizeNameKey(b.Name),
                StringComparison.OrdinalIgnoreCase));

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(BuildColumnDefinitionSql(column));
            }

            sb.Append(')');
            return sb.ToString();
        }

        internal static string BuildColumnDefinitionSql(ColumnModel column)
        {
            if (column == null)
            {
                throw new ArgumentNullException(nameof(column));
            }

            var sb = new StringBuilder();
            sb.Append(IdentifierHelper.EscapeIfKeyword(column.Name))
                .Append(' ')
                .Append(column.SqlType);

            if (column.IsIdentity)
            {
                sb.Append(" IDENTITY");
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultExpression))
            {
                sb.Append(" DEFAULT ").Append(column.DefaultExpression.Trim());
            }

            sb.Append(' ')
                .Append(column.IsNullable ? "NULL" : "NOT NULL");

            return sb.ToString();
        }

        internal static string BuildAddConstraintSql(TableModel table, ConstraintModel constraint)
        {
            var prefix = "ALTER TABLE " + table.Schema + "." + IdentifierHelper.EscapeIfKeyword(table.Name) +
                         " ADD CONSTRAINT " + IdentifierHelper.EscapeIfKeyword(constraint.Name) + " ";

            switch (constraint.Kind)
            {
                case ConstraintKind.PrimaryKey:
                    return prefix + "PRIMARY KEY (" + JoinColumns(constraint.Columns) + ")";
                case ConstraintKind.Unique:
                    return prefix + "UNIQUE (" + JoinColumns(constraint.Columns) + ")";
                case ConstraintKind.Check:
                    var definition = (constraint.Definition ?? string.Empty).Trim();
                    if (definition.Length == 0)
                    {
                        throw new InvalidOperationException("CHECK constraint definition is required.");
                    }

                    if (definition.StartsWith("(", StringComparison.Ordinal) && definition.EndsWith(")", StringComparison.Ordinal))
                    {
                        return prefix + "CHECK " + definition;
                    }

                    return prefix + "CHECK (" + definition + ")";
                case ConstraintKind.ForeignKey:
                    var referenceSchema = string.IsNullOrWhiteSpace(constraint.ReferenceSchema)
                        ? "dbo"
                        : constraint.ReferenceSchema;
                    return prefix + "FOREIGN KEY (" + JoinColumns(constraint.Columns) + ") REFERENCES " +
                           referenceSchema + "." + IdentifierHelper.EscapeIfKeyword(constraint.ReferenceTable) +
                           " (" + JoinColumns(constraint.ReferenceColumns) + ")";
                default:
                    throw new InvalidOperationException("Unsupported constraint kind.");
            }
        }

        internal static string BuildCreateIndexSql(TableModel table, IndexModel index)
        {
            var unique = index.IsUnique ? "UNIQUE " : string.Empty;
            var sql = "CREATE " + unique + "INDEX " + IdentifierHelper.EscapeIfKeyword(index.Name) + " ON " +
                      table.Schema + "." + IdentifierHelper.EscapeIfKeyword(table.Name) +
                      " (" + JoinIndexColumns(index.KeyColumns) + ")";

            if (index.IncludeColumns != null && index.IncludeColumns.Count > 0)
            {
                sql += " INCLUDE (" + JoinColumns(index.IncludeColumns) + ")";
            }

            return sql;
        }

        internal static string JoinColumns(IReadOnlyList<string> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return string.Empty;
            }

            var escaped = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i];
                escaped[i] = string.IsNullOrWhiteSpace(name) ? string.Empty : IdentifierHelper.EscapeIfKeyword(name);
            }

            return string.Join(", ", escaped);
        }

        internal static string JoinIndexColumns(IReadOnlyList<IndexKeyColumn> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (column == null || string.IsNullOrWhiteSpace(column.Name))
                {
                    parts[i] = string.Empty;
                    continue;
                }

                var name = IdentifierHelper.EscapeIfKeyword(column.Name);
                parts[i] = column.IsDescending ? name + " DESC" : name;
            }

            return string.Join(", ", parts);
        }
    }
}
