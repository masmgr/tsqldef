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
                case ExecuteStatement exec:
                    ProcessExecuteStatement(exec);
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

            string filterPredicate = null;
            if (node.FilterPredicate != null)
            {
                filterPredicate = GenerateScript(node.FilterPredicate).Trim();
            }

            Dictionary<string, string> parsedOptions = null;
            if (node.IndexOptions != null && node.IndexOptions.Count > 0)
            {
                parsedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var option in node.IndexOptions)
                {
                    ParseIndexOption(option, parsedOptions);
                }
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
            if (node.IncludeColumns?.Count > 0)
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
                IsClustered = node.Clustered == true,
                FilterPredicate = filterPredicate,
                KeyColumns = keyColumns,
                IncludeColumns = includeColumns.Count == 0 ? null : includeColumns,
                Options = parsedOptions,
            };

            table.Indexes[IdentifierHelper.NormalizeNameKey(index.Name)] = index;
        }

        private static void ParseIndexOption(IndexOption option, Dictionary<string, string> options)
        {
            var name = IdentifierHelper.NormalizeIndexOptionName(option.OptionKind.ToString());
            if (option is IndexExpressionOption exprOpt)
            {
                options[name] = (exprOpt.Expression as Literal)?.Value ?? string.Empty;
            }
            else if (option is IndexStateOption stateOpt)
            {
                options[name] = stateOpt.OptionState == OptionState.On ? "ON" : "OFF";
            }
            else
            {
                options[name] = GenerateScript(option).Trim();
            }
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
                Collation = column.Collation != null ? column.Collation.Value : null,
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

            if (column.DefaultConstraint != null)
            {
                var defaultName = column.DefaultConstraint.ConstraintIdentifier != null
                    && !string.IsNullOrWhiteSpace(column.DefaultConstraint.ConstraintIdentifier.Value)
                    ? column.DefaultConstraint.ConstraintIdentifier.Value
                    : "DF_" + table.Name + "_" + column.ColumnIdentifier.Value;

                var defaultConstraint = new ConstraintModel
                {
                    Kind = ConstraintKind.Default,
                    Name = defaultName,
                    Definition = columnModel.DefaultExpression,
                    DefaultColumnName = column.ColumnIdentifier.Value,
                };

                table.Constraints[IdentifierHelper.NormalizeNameKey(defaultConstraint.Name)] = defaultConstraint;
            }
        }

        private static bool ResolveNullability(ColumnDefinition column)
        {
            var nullableConstraint = column.Constraints
                .OfType<NullableConstraintDefinition>()
                .LastOrDefault();

            return nullableConstraint?.Nullable != false;
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
                DeleteAction = MapDeleteUpdateAction(foreignKey.DeleteAction),
                UpdateAction = MapDeleteUpdateAction(foreignKey.UpdateAction),
            };

            table.Constraints[IdentifierHelper.NormalizeNameKey(constraint.Name)] = constraint;
        }

        private void ProcessExecuteStatement(ExecuteStatement exec)
        {
            var procRef = exec.ExecuteSpecification?.ExecutableEntity as ExecutableProcedureReference;
            if (procRef == null)
            {
                throw CreateUnsupportedStatementException(exec);
            }

            var procName = procRef.ProcedureReference?.ProcedureReference?.Name;
            if (procName == null)
            {
                throw CreateUnsupportedStatementException(exec);
            }

            var fullName = procName.BaseIdentifier?.Value;
            if (!string.Equals(fullName, "sp_addextendedproperty", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedStatementException(exec);
            }

            ProcessSpAddExtendedProperty(exec, procRef);
        }

        private void ProcessSpAddExtendedProperty(ExecuteStatement exec, ExecutableProcedureReference procRef)
        {
            var parameters = procRef.Parameters;
            var paramName = GetNamedParameterStringValue(parameters, "@name");
            var paramValue = GetNamedParameterStringValue(parameters, "@value");
            var level0Type = GetNamedParameterStringValue(parameters, "@level0type");
            var level0Name = GetNamedParameterStringValue(parameters, "@level0name");
            var level1Type = GetNamedParameterStringValue(parameters, "@level1type");
            var level1Name = GetNamedParameterStringValue(parameters, "@level1name");
            var level2Type = GetNamedParameterStringValue(parameters, "@level2type");
            var level2Name = GetNamedParameterStringValue(parameters, "@level2name");

            if (!string.Equals(paramName, "MS_Description", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "ExtendedPropertyName");
            }

            if (!string.Equals(level0Type, "SCHEMA", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(level1Type, "TABLE", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "ExtendedPropertyLevel");
            }

            var schema = string.IsNullOrWhiteSpace(level0Name) ? _options.Schema : level0Name;
            if (!string.Equals(schema, _options.Schema, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedSchemaException(exec, schema);
            }

            if (string.IsNullOrWhiteSpace(level1Name))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "MissingTableName");
            }

            var tableKey = IdentifierHelper.BuildTableKey(_options.Schema, level1Name);
            if (!_model.Tables.TryGetValue(tableKey, out var table))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "TableNotFound");
            }

            if (string.IsNullOrWhiteSpace(level2Type))
            {
                table.Description = paramValue;
                return;
            }

            if (!string.Equals(level2Type, "COLUMN", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "ExtendedPropertyLevel2Type");
            }

            if (string.IsNullOrWhiteSpace(level2Name))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "MissingColumnName");
            }

            var columnKey = IdentifierHelper.NormalizeNameKey(level2Name);
            if (!table.Columns.TryGetValue(columnKey, out var column))
            {
                throw CreateUnsupportedFeatureException(exec, "ExecuteStatement", "ColumnNotFound");
            }

            column.Description = paramValue;
        }

        private static string GetNamedParameterStringValue(IList<ExecuteParameter> parameters, string variableName)
        {
            if (parameters == null)
            {
                return null;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                if (param.Variable != null &&
                    string.Equals(param.Variable.Name, variableName, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractStringLiteralValue(param.ParameterValue);
                }
            }

            return null;
        }

        private static string ExtractStringLiteralValue(ScalarExpression expression)
        {
            if (expression is StringLiteral stringLiteral)
            {
                return stringLiteral.Value;
            }

            return null;
        }

        private static string MapDeleteUpdateAction(DeleteUpdateAction action)
        {
            switch (action)
            {
                case DeleteUpdateAction.Cascade:
                    return "CASCADE";
                case DeleteUpdateAction.SetNull:
                    return "SET NULL";
                case DeleteUpdateAction.SetDefault:
                    return "SET DEFAULT";
                default:
                    return null;
            }
        }

        private (string Schema, string Name) ResolveSchemaAndName(SchemaObjectName name, TSqlFragment node)
        {
            if (name == null || name.BaseIdentifier == null)
            {
                throw CreateUnsupportedFeatureException(node, node.GetType().Name, "MissingObjectName");
            }

            var schemaIdentifier = name.SchemaIdentifier;
            var schemaName = schemaIdentifier == null ? _options.Schema : schemaIdentifier.Value;

            if (!string.Equals(schemaName, _options.Schema, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateUnsupportedSchemaException(node, schemaName);
            }

            return (_options.Schema, name.BaseIdentifier.Value);
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
                "Only CREATE TABLE / ALTER TABLE ... ADD ... / CREATE INDEX / EXEC sp_addextendedproperty are supported." + Environment.NewLine +
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
                "Schema mismatch: SQL uses schema '" + schemaName + "' but target schema is '" + _options.Schema + "'." + Environment.NewLine +
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
