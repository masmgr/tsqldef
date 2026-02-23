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
        DropConstraintsOnOriginal = 2,
        DropIndexesOnOriginal = 3,
        CopyData = 4,
        RenameOriginalToOld = 5,
        RenameShadowToOriginal = 6,
        RecreateConstraints = 7,
        RecreateIndexes = 8,
        DropOldTable = 9,
    }
}
