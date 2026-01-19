using System;
using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal sealed class CurrentSchemaReader
    {
        internal sealed class TableRow
        {
            public string SchemaName { get; set; }
            public string TableName { get; set; }
            public int ObjectId { get; set; }
        }

        internal sealed class ColumnRow
        {
            public int ObjectId { get; set; }
            public int ColumnId { get; set; }
            public string ColumnName { get; set; }
            public bool IsNullable { get; set; }
            public string TypeName { get; set; }
            public short MaxLength { get; set; }
            public byte Precision { get; set; }
            public byte Scale { get; set; }
            public bool IsComputed { get; set; }
            public bool IsIdentity { get; set; }
        }

        internal sealed class DefaultRow
        {
            public int ObjectId { get; set; }
            public int ColumnId { get; set; }
            public string DefaultDefinition { get; set; }
        }

        internal static DatabaseModel BuildModel(
            IEnumerable<TableRow> tables,
            IEnumerable<ColumnRow> columns,
            IEnumerable<DefaultRow> defaults = null)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var model = new DatabaseModel();
            var tableMap = new Dictionary<int, TableModel>();
            var defaultMap = BuildDefaultMap(defaults);

            foreach (var table in tables)
            {
                if (table == null) continue;

                if (!string.Equals(table.SchemaName, "dbo", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Current schema reader only supports dbo.");
                }

                var tableModel = model.GetOrAddTable("dbo", table.TableName);
                tableMap[table.ObjectId] = tableModel;
            }

            foreach (var column in columns)
            {
                if (column == null) continue;

                if (!tableMap.TryGetValue(column.ObjectId, out var table))
                {
                    throw new InvalidOperationException("Column row references an unknown table.");
                }

                var columnModel = new ColumnModel
                {
                    Name = column.ColumnName,
                    SqlType = SqlTypeFormatter.Format(
                        column.TypeName,
                        column.MaxLength,
                        column.Precision,
                        column.Scale),
                    IsNullable = column.IsNullable,
                    IsIdentity = column.IsIdentity,
                    DefaultExpression = GetDefaultDefinition(defaultMap, column.ObjectId, column.ColumnId),
                    IsFromAlterAdd = false,
                };

                table.Columns[IdentifierHelper.NormalizeNameKey(columnModel.Name)] = columnModel;
            }

            return model;
        }

        private static Dictionary<(int ObjectId, int ColumnId), string> BuildDefaultMap(IEnumerable<DefaultRow> defaults)
        {
            var map = new Dictionary<(int ObjectId, int ColumnId), string>();
            if (defaults == null)
            {
                return map;
            }

            foreach (var item in defaults)
            {
                if (item == null) continue;

                map[(item.ObjectId, item.ColumnId)] = item.DefaultDefinition;
            }

            return map;
        }

        private static string GetDefaultDefinition(
            Dictionary<(int ObjectId, int ColumnId), string> map,
            int objectId,
            int columnId)
        {
            if (map.TryGetValue((objectId, columnId), out var definition))
            {
                return definition;
            }

            return null;
        }
    }
}
