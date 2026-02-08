using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlSchemaDef.Core.Planning;

namespace SqlSchemaDef.SqlServer.Planning
{
    public sealed class DesiredSchemaLoader
    {
        public static DatabaseModel Load(string desiredSql)
        {
            if (desiredSql == null)
                throw new ArgumentNullException(nameof(desiredSql));

            return Load(desiredSql, new PlannerOptions(), out _);
        }

        public static DatabaseModel Load(string desiredSql, PlannerOptions options, out IReadOnlyList<SkippedItem> skipped)
        {
            if (desiredSql == null)
                throw new ArgumentNullException(nameof(desiredSql));
            options = options ?? new PlannerOptions();

            var model = new DatabaseModel();
            var visitor = new DesiredModelBuilderVisitor(model, options);

            var batches = BatchSplitter.Split(desiredSql);
            var fragments = DesiredSqlParser.ParseBatches(batches);

            for (var i = 0; i < fragments.Count; i++)
            {
                visitor.VisitBatch(fragments[i], batches[i].BatchIndex, batches[i].StartLine);
            }

            skipped = visitor.GetSkippedItems();
            return model;
        }
    }

    internal sealed class DesiredModelBuilderVisitor : TSqlFragmentVisitor
    {
        private static readonly SqlScriptGenerator ScriptGenerator = new Sql160ScriptGenerator();

        private readonly DatabaseModel _model;
        private readonly PlannerOptions _options;
        private readonly List<SkippedItem> _skipped = new List<SkippedItem>();
        private int _batchIndex;
        private int _batchStartLine;

        public DesiredModelBuilderVisitor(DatabaseModel model, PlannerOptions options)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IReadOnlyList<SkippedItem> GetSkippedItems() => _skipped;

        public void VisitBatch(TSqlFragment fragment, int batchIndex, int batchStartLine)
        {
            if (fragment == null)
                throw new ArgumentNullException(nameof(fragment));

            _batchIndex = batchIndex;
            _batchStartLine = batchStartLine;
            DispatchStatements(fragment);
        }

        private void DispatchStatements(TSqlFragment fragment)
        {
            if (fragment is TSqlScript script)
            {
                foreach (var batch in script.Batches)
                {
                    foreach (var statement in batch.Statements)
                    {
                        ProcessStatement(statement);
                    }
                }
                return;
            }

            if (fragment is TSqlBatch batchFragment)
            {
                foreach (var statement in batchFragment.Statements)
                {
                    ProcessStatement(statement);
                }
                return;
            }

            if (fragment is TSqlStatement statementFragment)
            {
                ProcessStatement(statementFragment);
                return;
            }

            throw CreateUnsupportedFeatureException(fragment, fragment.GetType().Name, "UnsupportedFragment");
        }

        private void ProcessStatement(TSqlStatement statement)
        {
            switch (statement)
            {
                case CreateTableStatement createTable:
                    Visit(createTable);
                    return;
                case AlterTableAddTableElementStatement alterAdd:
                    Visit(alterAdd);
                    return;
                case CreateIndexStatement createIndex:
                    Visit(createIndex);
                    return;
                default:
                    throw CreateUnsupportedStatementException(statement);
            }
        }

        public override void Visit(CreateTableStatement node)
        {
            var (schema, tableName) = ResolveSchemaAndName(node.SchemaObjectName, node);
            var table = _model.GetOrAddTable(schema, tableName);

            foreach (var column in node.Definition.ColumnDefinitions)
            {
                AddColumn(table, column, isFromAlterAdd: false, statementType: node.GetType().Name);
            }

            foreach (var constraint in node.Definition.TableConstraints)
            {
                AddConstraint(table, constraint, node.GetType().Name);
            }
        }

        public override void Visit(AlterTableAddTableElementStatement node)
        {
            var (schema, tableName) = ResolveSchemaAndName(node.SchemaObjectName, node);
            var table = _model.GetOrAddTable(schema, tableName);

            foreach (var column in node.Definition.ColumnDefinitions)
            {
                AddColumn(table, column, isFromAlterAdd: true, statementType: node.GetType().Name);
            }

            foreach (var constraint in node.Definition.TableConstraints)
            {
                AddConstraint(table, constraint, node.GetType().Name);
            }
        }

        public override void Visit(CreateIndexStatement node)
        {
            var (schema, tableName) = ResolveSchemaAndName(node.OnName, node);
            var table = _model.GetOrAddTable(schema, tableName);

            if (node.FilterPredicate != null)
            {
                throw CreateUnsupportedFeatureException(node, "CreateIndexStatement", "IndexFilter");
            }

            if (node.IndexOptions != null && node.IndexOptions.Count > 0)
            {
                throw CreateUnsupportedFeatureException(node, "CreateIndexStatement", "IndexOptions");
            }

            var keyColumns = new List<IndexKeyColumn>();
            foreach (var column in node.Columns)
            {
                var columnName = column.Column.MultiPartIdentifier.Identifiers.Last().Value;
                keyColumns.Add(new IndexKeyColumn
                {
                    Name = columnName,
                    IsDescending = column.SortOrder == SortOrder.Descending,
                });
            }

            var includeColumns = new List<string>();
            if (node.IncludeColumns != null && node.IncludeColumns.Count > 0)
            {
                foreach (var include in node.IncludeColumns)
                {
                    includeColumns.Add(include.MultiPartIdentifier.Identifiers.Last().Value);
                }
            }

            var index = new IndexModel
            {
                Name = RequireIdentifier(node.Name, node, "IndexName", "CreateIndexStatement"),
                IsUnique = node.Unique,
                KeyColumns = keyColumns,
                IncludeColumns = includeColumns.Count == 0 ? null : includeColumns,
            };

            table.Indexes[IdentifierHelper.NormalizeNameKey(index.Name)] = index;
        }

        private void AddColumn(TableModel table, ColumnDefinition column, bool isFromAlterAdd, string statementType)
        {
            if (column.ComputedColumnExpression != null)
            {
                throw CreateUnsupportedFeatureException(column, statementType, "ComputedColumn");
            }

            if (column.DataType == null)
            {
                throw CreateUnsupportedFeatureException(column, statementType, "MissingDataType");
            }

            var columnModel = new ColumnModel
            {
                Name = column.ColumnIdentifier.Value,
                SqlType = GenerateScript(column.DataType),
                IsNullable = ResolveNullability(column),
                IsIdentity = column.IdentityOptions != null,
                DefaultExpression = column.DefaultConstraint == null ? null : GenerateScript(column.DefaultConstraint.Expression),
                IsFromAlterAdd = isFromAlterAdd,
            };

            if (isFromAlterAdd &&
                !columnModel.IsNullable &&
                _options.NotNullColumnAddBehavior == NotNullColumnAddBehavior.Skip)
            {
                _skipped.Add(new SkippedItem
                {
                    Reason = SkippedReason.NotNullAddNotSupported,
                    Target = new SqlObjectRef
                    {
                        Type = SqlObjectType.Column,
                        Schema = table.Schema,
                        ParentName = table.Name,
                        Name = columnModel.Name,
                    },
                    Message = "NOT NULL column add is not supported in v1",
                });
                return;
            }

            table.Columns[IdentifierHelper.NormalizeNameKey(columnModel.Name)] = columnModel;
        }

        private static bool ResolveNullability(ColumnDefinition column)
        {
            var nullableConstraint = column.Constraints
                .OfType<NullableConstraintDefinition>()
                .LastOrDefault();

            return nullableConstraint == null || nullableConstraint.Nullable;
        }

        private void AddConstraint(TableModel table, ConstraintDefinition constraint, string statementType)
        {
            switch (constraint)
            {
                case UniqueConstraintDefinition unique:
                    AddUniqueConstraint(table, unique, statementType);
                    return;
                case CheckConstraintDefinition check:
                    AddCheckConstraint(table, check, statementType);
                    return;
                case ForeignKeyConstraintDefinition foreignKey:
                    AddForeignKeyConstraint(table, foreignKey, statementType);
                    return;
            }

            throw CreateUnsupportedFeatureException(constraint, statementType, "ConstraintType");
        }

        private void AddUniqueConstraint(TableModel table, UniqueConstraintDefinition unique, string statementType)
        {
            var columns = unique.Columns.Select(column => column.Column.MultiPartIdentifier.Identifiers.Last().Value).ToList();
            var constraint = new ConstraintModel
            {
                Kind = unique.IsPrimaryKey ? ConstraintKind.PrimaryKey : ConstraintKind.Unique,
                Name = RequireIdentifier(unique.ConstraintIdentifier, unique, "ConstraintName", statementType),
                Columns = columns,
            };

            table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
        }

        private void AddCheckConstraint(TableModel table, CheckConstraintDefinition check, string statementType)
        {
            var constraint = new ConstraintModel
            {
                Kind = ConstraintKind.Check,
                Name = RequireIdentifier(check.ConstraintIdentifier, check, "ConstraintName", statementType),
                Definition = GenerateScript(check.CheckCondition),
            };

            table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
        }

        private void AddForeignKeyConstraint(TableModel table, ForeignKeyConstraintDefinition foreignKey, string statementType)
        {
            var (schema, referenceTable) = ResolveSchemaAndName(foreignKey.ReferenceTableName, foreignKey);

            var constraint = new ConstraintModel
            {
                Kind = ConstraintKind.ForeignKey,
                Name = RequireIdentifier(foreignKey.ConstraintIdentifier, foreignKey, "ConstraintName", statementType),
                Columns = foreignKey.Columns.Select(column => column.Value).ToList(),
                ReferenceSchema = schema,
                ReferenceTable = referenceTable,
                ReferenceColumns = foreignKey.ReferencedTableColumns.Select(column => column.Value).ToList(),
            };

            if (foreignKey.DeleteAction != DeleteUpdateAction.NotSpecified ||
                foreignKey.UpdateAction != DeleteUpdateAction.NotSpecified)
            {
                throw CreateUnsupportedFeatureException(foreignKey, statementType, "ForeignKeyAction");
            }

            table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
        }

        private (string Schema, string Name) ResolveSchemaAndName(SchemaObjectName name, TSqlFragment node)
        {
            if (name == null || name.BaseIdentifier == null)
            {
                throw CreateUnsupportedFeatureException(node, node.GetType().Name, "MissingObjectName");
            }

            var schemaIdentifier = name.SchemaIdentifier;
            var schemaName = schemaIdentifier == null ? "dbo" : schemaIdentifier.Value;

            if (!string.Equals(schemaName, "dbo", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedSchemaException(node, schemaName);
            }

            return ("dbo", name.BaseIdentifier.Value);
        }

        private static string GenerateScript(TSqlFragment fragment)
        {
            ScriptGenerator.GenerateScript(fragment, out var text);
            return text.Trim();
        }

        private string RequireIdentifier(Identifier identifier, TSqlFragment node, string featureName, string statementType)
        {
            if (identifier == null || string.IsNullOrWhiteSpace(identifier.Value))
            {
                throw CreateUnsupportedFeatureException(node, statementType, featureName);
            }

            return identifier.Value;
        }

        private UnsupportedDesiredStatementException CreateUnsupportedStatementException(TSqlStatement node)
        {
            var (line, column) = GetLocation(node);
            var statementType = node.GetType().Name;
            var message =
                "Unsupported desired statement in v1 (additive-only)." + Environment.NewLine +
                "Only CREATE TABLE / ALTER TABLE ... ADD ... / CREATE INDEX are supported." + Environment.NewLine +
                $"Found: {statementType} at batch {_batchIndex}, line {line}, column {column}.";

            return new UnsupportedDesiredStatementException(message)
            {
                BatchIndex = _batchIndex,
                Line = line,
                Column = column,
                StatementType = statementType,
            };
        }

        private UnsupportedDesiredFeatureException CreateUnsupportedFeatureException(TSqlFragment node, string statementType, string featureName)
        {
            var (line, column) = GetLocation(node);
            var message =
                "Unsupported desired feature in v1 (additive-only)." + Environment.NewLine +
                $"Feature: {featureName}. Statement: {statementType}." + Environment.NewLine +
                $"Location: batch {_batchIndex}, line {line}, column {column}.";

            return new UnsupportedDesiredFeatureException(message)
            {
                BatchIndex = _batchIndex,
                Line = line,
                Column = column,
                StatementType = statementType,
                FeatureName = featureName,
            };
        }

        private UnsupportedSchemaException CreateUnsupportedSchemaException(TSqlFragment node, string schemaName)
        {
            var (line, column) = GetLocation(node);
            var message =
                "Unsupported schema in v1." + Environment.NewLine +
                "Only schema 'dbo' is supported. Found: '" + schemaName + "'." + Environment.NewLine +
                $"Location: batch {_batchIndex}, line {line}, column {column}.";

            return new UnsupportedSchemaException(message)
            {
                BatchIndex = _batchIndex,
                Line = line,
                Column = column,
                SchemaName = schemaName,
            };
        }

        private (int? Line, int? Column) GetLocation(TSqlFragment node)
        {
            var line = node.StartLine > 0 ? _batchStartLine + node.StartLine - 1 : (int?)null;
            var column = node.StartColumn > 0 ? node.StartColumn : (int?)null;
            return (line, column);
        }
    }
}
