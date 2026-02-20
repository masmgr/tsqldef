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
                    var fkSql = prefix + "FOREIGN KEY (" + JoinColumns(constraint.Columns) + ") REFERENCES " +
                           referenceSchema + "." + IdentifierHelper.EscapeIfKeyword(constraint.ReferenceTable) +
                           " (" + JoinColumns(constraint.ReferenceColumns) + ")";
                    if (!string.IsNullOrEmpty(constraint.DeleteAction))
                    {
                        fkSql += " ON DELETE " + constraint.DeleteAction;
                    }
                    if (!string.IsNullOrEmpty(constraint.UpdateAction))
                    {
                        fkSql += " ON UPDATE " + constraint.UpdateAction;
                    }
                    return fkSql;
                case ConstraintKind.Default:
                    var defExpr = (constraint.Definition ?? string.Empty).Trim();
                    if (defExpr.Length > 0 && !defExpr.StartsWith("(", StringComparison.Ordinal))
                    {
                        defExpr = "(" + defExpr + ")";
                    }
                    return prefix + "DEFAULT " + defExpr + " FOR " +
                           IdentifierHelper.EscapeIfKeyword(constraint.DefaultColumnName);
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

            if (index.IncludeColumns?.Count > 0)
            {
                sql += " INCLUDE (" + JoinColumns(index.IncludeColumns) + ")";
            }

            return sql;
        }

        internal static string BuildAddDescriptionSql(
            string schema, string tableName, string columnName, string description)
        {
            var sql = "EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'" +
                      EscapeSqlString(description) + "', @level0type = N'SCHEMA', @level0name = N'" +
                      schema + "', @level1type = N'TABLE', @level1name = N'" +
                      EscapeSqlString(tableName) + "'";

            if (columnName != null)
            {
                sql += ", @level2type = N'COLUMN', @level2name = N'" + EscapeSqlString(columnName) + "'";
            }

            return sql;
        }

        internal static string BuildUpdateDescriptionSql(
            string schema, string tableName, string columnName, string description)
        {
            var sql = "EXEC sp_updateextendedproperty @name = N'MS_Description', @value = N'" +
                      EscapeSqlString(description) + "', @level0type = N'SCHEMA', @level0name = N'" +
                      schema + "', @level1type = N'TABLE', @level1name = N'" +
                      EscapeSqlString(tableName) + "'";

            if (columnName != null)
            {
                sql += ", @level2type = N'COLUMN', @level2name = N'" + EscapeSqlString(columnName) + "'";
            }

            return sql;
        }

        private static string EscapeSqlString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            return value.Replace("'", "''");
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
