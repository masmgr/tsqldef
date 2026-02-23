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

**tsqldef** is a SQL Server schema migration tool that compares a desired DDL file against a live database and generates DDL scripts. Safe column alterations (type widening, nullability, collation changes) generate `ALTER TABLE ALTER COLUMN`; unsafe changes (type narrowing, IDENTITY) are skipped with optional rebuild proposals.

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

- **`DatabaseModel` / `TableModel` / `ColumnModel`** ([DesiredModel.cs](src/SqlSchemaDef.SqlServer/Planning/Model/DatabaseModel.cs)) — In-memory schema representation. Dictionary keys are case-insensitive (uppercased via `IdentifierHelper`).
- **`SchemaDiffer`** ([SchemaDiffer.cs](src/SqlSchemaDef.SqlServer/Planning/Diffing/SchemaDiffer.cs)) — Core diffing logic. `internal` class; tests access via `InternalsVisibleTo`.
- **`SqlStatementBuilder`** ([SqlStatementBuilder.cs](src/SqlSchemaDef.SqlServer/Planning/Diffing/SqlStatementBuilder.cs)) — Shared SQL generation (`internal static`). All identifiers are bracket-escaped via `IdentifierHelper.Escape()`.
- **`DesiredSchemaLoader`** ([DesiredSchemaLoader.cs](src/SqlSchemaDef.SqlServer/Planning/Parsing/DesiredSchemaLoader.cs)) — Public entry point for parsing desired SQL into `DatabaseModel`. Wraps `DesiredModelBuilderVisitor`.
- **`SqlTypeWideningSafety`** ([SqlTypeWideningSafety.cs](src/SqlSchemaDef.SqlServer/Planning/Diffing/SqlTypeWideningSafety.cs)) — Determines whether a column type change is a safe widening conversion (e.g., `int`→`bigint`, `varchar(50)`→`varchar(100)`).
- **`RebuildProposalBuilder`** ([RebuildProposalBuilder.cs](src/SqlSchemaDef.SqlServer/Planning/Diffing/RebuildProposalBuilder.cs)) — Generates shadow-table rebuild steps for column alterations that can't be done via ALTER COLUMN.
- **`MigrationPlanSerializer`** ([MigrationPlanSerializer.cs](src/SqlSchemaDef.Core/Planning/MigrationPlanSerializer.cs)) — JSON serialization (Newtonsoft.Json, camelCase, enums as strings, nulls ignored).

### DDL Feature Coverage

Supported:
- Tables and columns (CREATE TABLE, ALTER TABLE ADD COLUMN)
- Column options: data types, NULL/NOT NULL, IDENTITY, DEFAULT expression, COLLATE
- **ALTER TABLE ALTER COLUMN** for safe column changes:
  - Type widening (int→bigint, varchar(50)→varchar(100), decimal precision increase, etc.)
  - Nullability changes (NULL↔NOT NULL)
  - Collation changes
- Primary key, UNIQUE, CHECK, FOREIGN KEY constraints (with ON DELETE/UPDATE actions)
- Indexes: CREATE [UNIQUE] [CLUSTERED|NONCLUSTERED] INDEX, INCLUDE columns, WHERE (filtered), WITH options (FILLFACTOR, PAD_INDEX, etc.)
- MS_Description extended properties (tables and columns)
- Strict mode, JSON plan output, scope filters
- Non-dbo schemas via `--schema` option (schema-less DDL falls back to target schema; cross-schema mismatch raises `UnsupportedSchemaException`)
- All SQL identifiers unconditionally bracket-escaped in generated DDL (`[schema].[table]` notation throughout)
- **DROP TABLE** via `--allow-drop` flag: tables in the current database but absent from the desired DDL are dropped. Foreign keys from other tables referencing the dropped table are automatically cascade-dropped.

Not supported (skipped or rejected):
- Unsafe column alterations: type narrowing (bigint→int), IDENTITY changes, cross-family type changes (varchar→int) — these generate rebuild proposals via `--emit-swap-sql`
- Computed columns
- PARTITION schemes
- Columnstore indexes
- XML/spatial indexes
- Row-level security, masked columns
- Triggers, views, stored procedures, functions

## Conventions

### Development Workflow
- TDD: Red → Green → Refactor with small commits
- CLI tests call `Program.Main(string[])` directly, capturing stdout/stderr via `Console.SetOut`/`Console.SetError`
- Integration tests are tagged `[Collection(SqlServerIntegrationGroup.Name)]` and need `SQLSCHEMADEF_TEST_CONNECTION_STRING`
- Property-based tests (FsCheck) live in `tests/SqlSchemaDef.Tests/Properties/`

### CLI Usage
```
SqlSchemaDef.Cli export --connection <cs> [--out <desired.sql>] [--schema <schema>]
SqlSchemaDef.Cli plan   --connection <cs> --file <desired.sql> [--schema <schema>] [--format script|json] [--strict] [--emit-swap-sql] [--allow-drop] [--include <tables>] [--exclude <tables>]
SqlSchemaDef.Cli apply  --connection <cs> (--file <desired.sql> | --plan <plan.json>) [--schema <schema>] [--allow-drop] [--include <tables>] [--exclude <tables>]
```
- `--schema` defaults to `dbo` when omitted
- `--allow-drop` enables DROP TABLE for tables not in the desired DDL (without this flag, surplus tables are reported as skipped items)
- `--strict` exits non-zero (30) if any skipped items exist
- `--emit-swap-sql` generates rebuild proposals for non-additive diffs
- `--format script|json` controls plan output format (default: `script`)

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

### SQL Identifier Escaping
- **`IdentifierHelper.Escape(name)`** — unconditionally wraps in `[brackets]` (replaces `]` with `]]`). Use this in all SQL generation paths.
- **`IdentifierHelper.EscapeIfKeyword(name)`** — escapes only if the identifier is a reserved keyword (retained for backward compatibility; used in property-based tests only).
- Generated SQL always uses `[schema].[table]` (`[column]`) notation.

### ScriptDom Notes (v170 / TSql160Parser)
- `CreateIndexStatement.Clustered` is `bool?` (nullable) — use `node.Clustered == true`
- `ColumnDefinitionBase.Collation` is `Identifier` type — use `.Value` for the string
- Index option types: `IndexExpressionOption` (not `LiteralIndexOption`) and `IndexStateOption` (not `OnOffIndexOption`)
- `IndexOptionKind.SortInTempDB` — note uppercase "DB"

### Schema Handling
- `PlannerOptions.Schema` defaults to `"dbo"`; override via `--schema` CLI flag
- DDL without explicit schema (e.g., `CREATE TABLE T ...`) falls back to `options.Schema`
- Cross-schema mismatch (DDL schema ≠ target schema) raises `UnsupportedSchemaException` (exit code 11)

### Column Change Classification

`SchemaDiffer.ClassifyColumnChange()` categorizes column differences into four kinds:
- **`None`** — no difference
- **`SafeAlter`** — can be done via `ALTER TABLE ALTER COLUMN` (type widening, nullability, collation)
- **`UnsafeAlter`** — requires rebuild (IDENTITY change, type narrowing, cross-family type change)
- **`DefaultOnly`** — only DEFAULT expression differs; delegated to constraint diff (`RecreateConstraint`)

`SqlTypeWideningSafety.IsSafeTypeChange()` determines type safety across 8 type families: Integer, Money, Float, NonUnicodeString, UnicodeString, Binary, DateTime, Decimal.

### Milestone History
- v0.1: export/plan/apply for tables + columns + indexes
- v0.2: constraints (PK, UNIQUE, CHECK) + foreign keys
- v0.3: strict mode, JSON plan output, scope filters
- v0.4: rebuild proposals / shadow-table swap SQL generation
- v0.5: MS_Description extended properties (tables and columns)
- v0.6: COLLATE on columns, filtered indexes (WHERE), clustered indexes, index options (WITH)
- post-v0.6: non-dbo schema support (`--schema` option), unconditional bracket-escaping of all identifiers, index metadata/option normalization fixes
- v0.7.1: ALTER TABLE ALTER COLUMN for safe column changes (type widening, nullability, collation)
- v0.8: DROP TABLE via `--allow-drop` flag with automatic cascade FK drop
