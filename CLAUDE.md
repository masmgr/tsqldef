# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build
dotnet build SqlSchemaDef.sln

# Run all non-integration tests
dotnet test SqlSchemaDef.sln --filter "Category!=Integration"

# Run all tests (requires SQL Server; auto-detects LocalDB on Windows, or set SQLSCHEMADEF_TEST_CONNECTION_STRING)
dotnet test SqlSchemaDef.sln

# Run a single test class
dotnet test SqlSchemaDef.sln --filter "FullyQualifiedName~SchemaDifferTests"

# Run a single test method
dotnet test SqlSchemaDef.sln --filter "FullyQualifiedName~SchemaDifferTests.MethodName"

# Build Release (as CI does)
dotnet build SqlSchemaDef.sln -c Release --no-restore
```

There is no separate lint command; Roslyn analyzers (Microsoft.CodeAnalysis.NetAnalyzers + StyleCop.Analyzers) run during build and emit warnings/errors.

## Architecture

**tsqldef** is a SQL Server schema migration tool that compares a desired DDL file against a live database and generates additive-only DDL scripts. v1 scope is strictly additive — no ALTER, DROP, or destructive changes.

### Projects

| Project | Target | Role |
|---------|--------|------|
| `SqlSchemaDef.Core` | netstandard2.0 | Domain types, interfaces, plan serialization. Only dependency: Newtonsoft.Json |
| `SqlSchemaDef.SqlServer` | netstandard2.0 | SQL Server implementation using ScriptDom (T-SQL parser) and SqlClient |
| `SqlSchemaDef.Cli` | net8.0 | CLI entry point with three commands: `export`, `plan`, `apply` |
| `SqlSchemaDef.Tests` | net8.0 | xUnit + FsCheck property-based tests |

### Data Flow (plan command)

```
desired.sql → BatchSplitter → DesiredSqlParser → DesiredModelBuilderVisitor → DatabaseModel
                                                                                    ↓
live DB → CurrentSchemaCatalogReader → CurrentSchemaModelBuilder → DatabaseModel → SchemaDiffer → MigrationPlan
```

`MigrationPlan` contains `Operations` (executable SQL), `Skipped` items (things v1 can't do), and optional `Proposals` (rebuild suggestions for column alterations).

### Key Internal Types

- **`DatabaseModel` / `TableModel` / `ColumnModel`** ([DesiredModel.cs](src/SqlSchemaDef.SqlServer/Planning/DesiredModel.cs)) — In-memory schema representation. Dictionary keys are case-insensitive (uppercased via `IdentifierHelper`).
- **`SchemaDiffer`** ([SchemaDiffer.cs](src/SqlSchemaDef.SqlServer/Planning/SchemaDiffer.cs)) — Core diffing logic. `internal` class; tests access via `InternalsVisibleTo`.
- **`SqlStatementBuilder`** ([SqlStatementBuilder.cs](src/SqlSchemaDef.SqlServer/Planning/SqlStatementBuilder.cs)) — Shared SQL generation (`internal static`).
- **`RebuildProposalBuilder`** ([RebuildProposalBuilder.cs](src/SqlSchemaDef.SqlServer/Planning/RebuildProposalBuilder.cs)) — Generates shadow-table rebuild steps for column alterations that can't be done additively.
- **`MigrationPlanSerializer`** ([MigrationPlanSerializer.cs](src/SqlSchemaDef.Core/Planning/MigrationPlanSerializer.cs)) — JSON serialization (Newtonsoft.Json, camelCase, enums as strings, nulls ignored).

## Conventions

### Development Workflow
- TDD: Red → Green → Refactor with small commits
- CLI tests call `Program.Main(string[])` directly, capturing stdout/stderr via `Console.SetOut`/`Console.SetError`
- Integration tests are tagged `[Collection(SqlServerIntegrationGroup.Name)]` and need `SQLSCHEMADEF_TEST_CONNECTION_STRING`
- Property-based tests (FsCheck) live in `tests/SqlSchemaDef.Tests/Properties/`

### Exit Codes
0=OK, 2=usage error, 10=parse error, 11=unsupported statement, 20=apply failed, 30=strict mode violation

### netstandard2.0 Constraint
Core and SqlServer projects target netstandard2.0 — no `record`, `required`, or nullable reference type annotations in their public APIs.

### Analyzer Rules to Watch
- **SA1515**: Blank line required before single-line comments
- **CA1861**: Use `static readonly` arrays (relaxed to suggestion in test code)
- **CA1825**: Use `Array.Empty<T>()` instead of `new T[0]`

### Naming
- Private fields: `camelCase` (underscore prefix `_field` is allowed, SA1309 suppressed)
- Test methods: `Method_Scenario_Expected` pattern (CA1707 suppressed in tests)
- Dictionary keys: always normalized via `IdentifierHelper.NormalizeNameKey()` / `BuildTableKey()`
