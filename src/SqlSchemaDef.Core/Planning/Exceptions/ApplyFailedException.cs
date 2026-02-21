using System;

namespace SqlSchemaDef.Core.Planning
{
    public sealed class ApplyFailedException : Exception
    {
        public ApplyFailedException(string message, SqlOperation operation, Exception inner)
            : base(message, inner) => Operation = operation;

        public SqlOperation Operation { get; }
    }

    public sealed class RebuildFailedException : Exception
    {
        public RebuildFailedException(string message, RebuildProposal proposal, RebuildStep step, Exception inner)
            : base(message, inner)
        {
            Proposal = proposal;
            Step = step;
        }

        public RebuildProposal Proposal { get; }

        public RebuildStep Step { get; }
    }
}
