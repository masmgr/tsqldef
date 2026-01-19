using System;
using System.Collections.Generic;

namespace SqlSchemaDef.Core.Planning
{
    public sealed class DesiredSqlParseException : Exception
    {
        public DesiredSqlParseException(string message, IReadOnlyList<SqlDiagnostic> diagnostics, Exception inner = null)
            : base(message, inner) => Diagnostics = diagnostics ?? Array.Empty<SqlDiagnostic>();

        public IReadOnlyList<SqlDiagnostic> Diagnostics { get; }
    }

    public sealed class SqlDiagnostic
    {
        public int? BatchIndex { get; set; }
        public string Message { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
        public string Fragment { get; set; }
    }
}

