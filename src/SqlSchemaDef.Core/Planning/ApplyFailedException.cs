using System;

namespace SqlSchemaDef.Core.Planning
{
    public sealed class ApplyFailedException : Exception
    {
        public ApplyFailedException(string message, SqlOperation operation, Exception inner)
            : base(message, inner) => Operation = operation;

        public SqlOperation Operation { get; }
    }
}

