using System;
using System.Collections.Generic;
using System.Globalization;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class CurrentSchemaModelBuilderHelpers
    {
        internal static string GetDefaultDefinition(
            Dictionary<(int ObjectId, int ColumnId), string> map,
            int objectId,
            int columnId)
        {
            if (map.TryGetValue((objectId, columnId), out var definition))
            {
                return definition;
            }

            return null;
        }

        internal static ConstraintKind ResolveKeyConstraintKind(string constraintType)
        {
            if (string.Equals(constraintType, "PK", StringComparison.OrdinalIgnoreCase))
            {
                return ConstraintKind.PrimaryKey;
            }

            if (string.Equals(constraintType, "UQ", StringComparison.OrdinalIgnoreCase))
            {
                return ConstraintKind.Unique;
            }

            throw new InvalidOperationException("Unsupported key constraint type.");
        }

        internal static string ResolveReferenceSchema(string referencedSchemaName)
        {
            return string.IsNullOrWhiteSpace(referencedSchemaName) ? "dbo" : referencedSchemaName;
        }

        internal static Dictionary<string, string> BuildIndexOptionsFromRow(CurrentSchemaReader.IndexRow row)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (row.FillFactor > 0)
            {
                options["FILLFACTOR"] = row.FillFactor.ToString(CultureInfo.InvariantCulture);
            }

            if (row.IsPadded)
            {
                options["PAD_INDEX"] = "ON";
            }

            if (row.IgnoreDupKey)
            {
                options["IGNORE_DUP_KEY"] = "ON";
            }

            if (!row.AllowRowLocks)
            {
                options["ALLOW_ROW_LOCKS"] = "OFF";
            }

            if (!row.AllowPageLocks)
            {
                options["ALLOW_PAGE_LOCKS"] = "OFF";
            }

            if (row.NoRecompute)
            {
                options["STATISTICS_NORECOMPUTE"] = "ON";
            }

            return options.Count == 0 ? null : options;
        }
    }
}
