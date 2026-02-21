using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class IndexModel
    {
        public string Name { get; set; }
        public bool IsUnique { get; set; }
        public bool IsClustered { get; set; }
        public string FilterPredicate { get; set; }
        public IReadOnlyList<IndexKeyColumn> KeyColumns { get; set; }
        public IReadOnlyList<string> IncludeColumns { get; set; }
        public IDictionary<string, string> Options { get; set; }
        public string UnsupportedFeature { get; set; }
    }

    public sealed class IndexKeyColumn
    {
        public string Name { get; set; }
        public bool IsDescending { get; set; }
    }
}
