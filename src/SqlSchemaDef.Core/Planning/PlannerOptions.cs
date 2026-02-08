namespace SqlSchemaDef.Core.Planning
{
    public sealed class PlannerOptions
    {
        public string Schema { get; set; } = "dbo";

        public UnsupportedDesiredStatementBehavior UnsupportedDesiredStatementBehavior { get; set; }
            = UnsupportedDesiredStatementBehavior.Error;

        public PlanMode Mode { get; set; } = PlanMode.AdditiveOnly;

        public NotNullColumnAddBehavior NotNullColumnAddBehavior { get; set; }
            = NotNullColumnAddBehavior.Skip;

        public SurplusCurrentObjectBehavior SurplusCurrentObjectBehavior { get; set; }
            = SurplusCurrentObjectBehavior.CollectAsSkipped;
    }

    public enum PlanMode
    {
        AdditiveOnly = 0,
    }

    public enum UnsupportedDesiredStatementBehavior
    {
        Error = 0,
    }

    public enum NotNullColumnAddBehavior
    {
        Skip = 0,
    }

    public enum SurplusCurrentObjectBehavior
    {
        CollectAsSkipped = 0,
    }
}
