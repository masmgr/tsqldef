using System.Collections.Generic;

namespace SqlSchemaDef.SqlServer.Planning
{
    internal static class CurrentSchemaRowGrouping
    {
        internal static Dictionary<(int ObjectId, int ColumnId), string> BuildDefaultMap(
            IEnumerable<CurrentSchemaReader.DefaultRow> defaults)
        {
            var map = new Dictionary<(int ObjectId, int ColumnId), string>();
            if (defaults == null)
            {
                return map;
            }

            foreach (var item in defaults)
            {
                if (item == null)
                    continue;

                map[(item.ObjectId, item.ColumnId)] = item.DefaultDefinition;
            }

            return map;
        }

        internal static Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.KeyConstraintRow>> BuildKeyConstraintGroups(
            IEnumerable<CurrentSchemaReader.KeyConstraintRow> keyConstraints)
        {
            var groups = new Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.KeyConstraintRow>>();
            if (keyConstraints == null)
            {
                return groups;
            }

            foreach (var item in keyConstraints)
            {
                if (item == null)
                    continue;

                var key = (item.ObjectId, item.ConstraintName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<CurrentSchemaReader.KeyConstraintRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        internal static Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.CheckConstraintRow>> BuildCheckConstraintGroups(
            IEnumerable<CurrentSchemaReader.CheckConstraintRow> checkConstraints)
        {
            var groups = new Dictionary<(int ObjectId, string ConstraintName), List<CurrentSchemaReader.CheckConstraintRow>>();
            if (checkConstraints == null)
            {
                return groups;
            }

            foreach (var item in checkConstraints)
            {
                if (item == null)
                    continue;

                var key = (item.ObjectId, item.ConstraintName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<CurrentSchemaReader.CheckConstraintRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        internal static Dictionary<(int ParentObjectId, string ConstraintName), List<CurrentSchemaReader.ForeignKeyRow>> BuildForeignKeyGroups(
            IEnumerable<CurrentSchemaReader.ForeignKeyRow> foreignKeys)
        {
            var groups = new Dictionary<(int ParentObjectId, string ConstraintName), List<CurrentSchemaReader.ForeignKeyRow>>();
            if (foreignKeys == null)
            {
                return groups;
            }

            foreach (var item in foreignKeys)
            {
                if (item == null)
                    continue;

                var key = (item.ParentObjectId, item.ConstraintName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<CurrentSchemaReader.ForeignKeyRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }

        internal static Dictionary<(int ObjectId, string IndexName), List<CurrentSchemaReader.IndexRow>> BuildIndexGroups(
            IEnumerable<CurrentSchemaReader.IndexRow> indexes)
        {
            var groups = new Dictionary<(int ObjectId, string IndexName), List<CurrentSchemaReader.IndexRow>>();
            if (indexes == null)
            {
                return groups;
            }

            foreach (var item in indexes)
            {
                if (item == null)
                    continue;

                var key = (item.ObjectId, item.IndexName ?? string.Empty);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<CurrentSchemaReader.IndexRow>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            return groups;
        }
    }
}
