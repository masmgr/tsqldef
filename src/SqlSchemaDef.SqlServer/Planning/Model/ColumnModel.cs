namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class ColumnModel
    {
        public string Name { get; set; }
        public string SqlType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public string DefaultExpression { get; set; }
        public bool IsFromAlterAdd { get; set; }
        public string UnsupportedFeature { get; set; }
        public string Collation { get; set; }
        public string Description { get; set; }
    }
}
