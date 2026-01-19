using System;

namespace SqlSchemaDef.Core.Planning
{
    public abstract class DesiredSqlException : Exception
    {
        protected DesiredSqlException(string message, Exception inner = null) : base(message, inner) { }

        public int? BatchIndex { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
    }

    public sealed class UnsupportedDesiredStatementException : DesiredSqlException
    {
        public UnsupportedDesiredStatementException(string message) : base(message) { }
        public string StatementType { get; set; }
    }

    public sealed class UnsupportedDesiredFeatureException : DesiredSqlException
    {
        public UnsupportedDesiredFeatureException(string message) : base(message) { }
        public string StatementType { get; set; }
        public string FeatureName { get; set; }
    }

    public sealed class UnsupportedSchemaException : DesiredSqlException
    {
        public UnsupportedSchemaException(string message) : base(message) { }
        public string SchemaName { get; set; }
    }

    public sealed class UnsupportedBatchSeparatorException : DesiredSqlException
    {
        public UnsupportedBatchSeparatorException(string message) : base(message) { }
        public string SeparatorText { get; set; }
    }
}

