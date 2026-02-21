using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
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
}
