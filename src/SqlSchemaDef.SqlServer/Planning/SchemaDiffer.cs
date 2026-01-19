using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class SchemaDiffer
    {
        public MigrationPlan Diff(
            DatabaseModel current,
            DatabaseModel desired,
            PlanMetadata metadata,
            PlannerOptions options = null)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (desired == null) throw new ArgumentNullException(nameof(desired));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            options = options ?? new PlannerOptions();

            var operations = new List<SqlOperation>();
            var skipped = new List<SkippedItem>();

            foreach (var desiredEntry in desired.Tables.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var desiredTable = desiredEntry.Value;
                var tableKey = IdentifierHelper.BuildTableKey(desiredTable.Schema, desiredTable.Name);

                if (!current.Tables.TryGetValue(tableKey, out var currentTable))
                {
                    operations.Add(CreateTableOperation(desiredTable));
                    continue;
                }

                foreach (var columnEntry in desiredTable.Columns.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var desiredColumn = columnEntry.Value;
                    if (currentTable.Columns.ContainsKey(columnEntry.Key))
                    {
                        continue;
                    }

                    if (!desiredColumn.IsNullable && options.NotNullColumnAddBehavior == NotNullColumnAddBehavior.Skip)
                    {
                        skipped.Add(new SkippedItem
                        {
                            Reason = SkippedReason.NotNullAddNotSupported,
                            Target = new SqlObjectRef
                            {
                                Type = SqlObjectType.Column,
                                Schema = desiredTable.Schema,
                                ParentName = desiredTable.Name,
                                Name = desiredColumn.Name,
                            },
                            Message = "NOT NULL column add is not supported in v1",
                        });
                        continue;
                    }

                    operations.Add(AddColumnOperation(desiredTable, desiredColumn));
                }
            }

            return new MigrationPlan(metadata, operations, skipped);
        }

        private static SqlOperation CreateTableOperation(TableModel table)
        {
            var sql = BuildCreateTableSql(table);
            return new SqlOperation
            {
                Kind = OperationKind.CreateTable,
                Description = "Create table dbo." + table.Name,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Table,
                    Schema = table.Schema,
                    Name = table.Name,
                },
            };
        }

        private static SqlOperation AddColumnOperation(TableModel table, ColumnModel column)
        {
            var sql = "ALTER TABLE " + table.Schema + "." + table.Name +
                      " ADD " + column.Name + " " + column.SqlType + " " +
                      (column.IsNullable ? "NULL" : "NOT NULL");

            return new SqlOperation
            {
                Kind = OperationKind.AddColumn,
                Description = "Add column " + table.Schema + "." + table.Name + "." + column.Name,
                Sql = sql,
                Target = new SqlObjectRef
                {
                    Type = SqlObjectType.Column,
                    Schema = table.Schema,
                    ParentName = table.Name,
                    Name = column.Name,
                },
            };
        }

        private static string BuildCreateTableSql(TableModel table)
        {
            var sb = new StringBuilder();
            sb.Append("CREATE TABLE ").Append(table.Schema).Append(".").Append(table.Name).Append(" (");

            var columns = table.Columns.Values
                .OrderBy(column => IdentifierHelper.NormalizeNameKey(column.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(column.Name)
                    .Append(" ")
                    .Append(column.SqlType)
                    .Append(" ")
                    .Append(column.IsNullable ? "NULL" : "NOT NULL");
            }

            sb.Append(")");
            return sb.ToString();
        }
    }
}
