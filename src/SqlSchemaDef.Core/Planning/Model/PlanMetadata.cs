using System;

namespace SqlSchemaDef.Core.Planning
{
    public sealed class PlanMetadata
    {
        public int PlanFormatVersion { get; set; } = 1;
        public string Schema { get; set; } = "dbo";
        public DateTimeOffset PlannedAt { get; set; }
        public string PlannerVersion { get; set; }
        public string DatabaseName { get; set; }
        public string ServerVersion { get; set; }
    }
}
