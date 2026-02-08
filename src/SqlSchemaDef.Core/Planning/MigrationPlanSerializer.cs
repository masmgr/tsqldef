using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace SqlSchemaDef.Core.Planning
{
    public static class MigrationPlanSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static string ToJson(MigrationPlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            var dto = new MigrationPlanDto
            {
                Metadata = plan.Metadata,
                Operations = plan.Operations,
                Skipped = plan.Skipped,
            };

            return JsonConvert.SerializeObject(dto, Settings);
        }

        public static MigrationPlan FromJson(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            MigrationPlanDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<MigrationPlanDto>(json, Settings);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid JSON plan: " + ex.Message, ex);
            }

            if (dto == null)
            {
                throw new InvalidOperationException("Invalid JSON plan: deserialization returned null.");
            }

            var version = dto.Metadata != null ? dto.Metadata.PlanFormatVersion : 0;
            if (version < 1 || version > 1)
            {
                throw new InvalidOperationException(
                    "Unsupported plan format version: " + version + ". Only version 1 is supported.");
            }

            return new MigrationPlan(
                dto.Metadata ?? new PlanMetadata(),
                dto.Operations ?? (IReadOnlyList<SqlOperation>)Array.Empty<SqlOperation>(),
                dto.Skipped ?? (IReadOnlyList<SkippedItem>)Array.Empty<SkippedItem>());
        }

        private sealed class MigrationPlanDto
        {
            public PlanMetadata Metadata { get; set; }
            public IReadOnlyList<SqlOperation> Operations { get; set; }
            public IReadOnlyList<SkippedItem> Skipped { get; set; }
        }
    }
}
