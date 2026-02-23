using System;
using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class TableModel
    {
        public TableModel(string schema, string name)
        {
            Schema = schema;
            Name = name;
            Columns = new Dictionary<string, ColumnModel>(StringComparer.OrdinalIgnoreCase);
            ColumnOrder = new List<string>();
            Constraints = new Dictionary<string, ConstraintModel>(StringComparer.OrdinalIgnoreCase);
            Indexes = new Dictionary<string, IndexModel>(StringComparer.OrdinalIgnoreCase);
        }

        public string Schema { get; }
        public string Name { get; }
        public IDictionary<string, ColumnModel> Columns { get; }
        public List<string> ColumnOrder { get; }
        public IDictionary<string, ConstraintModel> Constraints { get; }
        public IDictionary<string, IndexModel> Indexes { get; }
        public string Description { get; set; }
    }
}
