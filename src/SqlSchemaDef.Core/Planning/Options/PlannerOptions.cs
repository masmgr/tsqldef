using System.Collections.Generic;

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

        public bool EmitProposals { get; set; }

        public IReadOnlyList<string> IncludeTablePatterns { get; set; }

        public IReadOnlyList<string> ExcludeTablePatterns { get; set; }

        public bool AllowDrop { get; set; }

        public bool ReorderColumns { get; set; }
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
