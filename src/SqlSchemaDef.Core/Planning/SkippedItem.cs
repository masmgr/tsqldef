namespace SqlSchemaDef.Core.Planning
{
    public sealed class SkippedItem
    {
        public SkippedReason Reason { get; set; }
        public string Message { get; set; }
        public SqlObjectRef Target { get; set; }
        public string Details { get; set; }
    }

    public enum SkippedReason
    {
        DropNotSupported = 1,
        AlterNotSupported = 2,
        NotNullAddNotSupported = 3,
        UnsupportedFeatureInDesired = 4,
    }
}

