namespace SqlSchemaDef.Core.Planning
{
    public sealed class SqlObjectRef
    {
        public string Schema { get; set; } = "dbo";
        public SqlObjectType Type { get; set; }
        public string Name { get; set; }
        public string ParentName { get; set; }

        public string ToDisplayName()
        {
            var schema = string.IsNullOrEmpty(Schema) ? "dbo" : Schema;

            switch (Type)
            {
                case SqlObjectType.Table:
                    return schema + "." + (Name ?? "(unknown)");
                case SqlObjectType.Column:
                    return schema + "." + (ParentName ?? "(unknown)") + "." + (Name ?? "(unknown)");
                case SqlObjectType.Constraint:
                    return schema + "." + (ParentName ?? "(unknown)") + "." + (Name ?? "(unknown)");
                case SqlObjectType.Index:
                    return schema + "." + (ParentName ?? "(unknown)") + "." + (Name ?? "(unknown)");
                case SqlObjectType.ForeignKey:
                    return schema + "." + (ParentName ?? "(unknown)") + "." + (Name ?? "(unknown)");
                case SqlObjectType.Description:
                    if (!string.IsNullOrEmpty(ParentName))
                        return schema + "." + ParentName + "." + (Name ?? "(unknown)");
                    return schema + "." + (Name ?? "(unknown)");
                default:
                    return schema + "." + (Name ?? "(unknown)");
            }
        }
    }

    public enum SqlObjectType
    {
        Table = 1,
        Column = 2,
        Constraint = 3,
        Index = 4,
        ForeignKey = 5,
        Description = 6,
    }
}
