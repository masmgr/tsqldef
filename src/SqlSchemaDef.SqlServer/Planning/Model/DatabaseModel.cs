using System;
using System.Collections.Generic;

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
}
