using System.Collections.Generic;

namespace SqlSchemaDef.Core.Planning
{
    public sealed class RebuildProposal
    {
        public SqlObjectRef Target { get; set; }
        public string Description { get; set; }
        public string Warning { get; set; }
        public IReadOnlyList<RebuildStep> Steps { get; set; }
        public string Script { get; set; }
    }

    public sealed class RebuildStep
    {
        public RebuildStepKind Kind { get; set; }
        public string Description { get; set; }
        public string Sql { get; set; }
    }

    public enum RebuildStepKind
    {
        CreateShadowTable = 1,
        CopyData = 2,
        DropConstraintsOnOriginal = 3,
        RenameOriginalToOld = 4,
        RenameShadowToOriginal = 5,
        RecreateConstraints = 6,
        RecreateIndexes = 7,
        DropOldTable = 8,
    }
}
