# SQL Server-only sqldef equivalent (C#/.NET + ScriptDom) v1 Plan

Goal: provide a .NET-friendly way to align a SQL Server schema to **DDL (`desired`)** by computing the difference from the current database (**`current`**) and applying it **idempotently**.

This document summarizes the v1 design and implementation plan. v1 is “safety first” and performs **new creations + additive changes only**.

Targets:
- The library targets **.NET Standard 2.0**
  - Assume only BCL types and APIs available within that target
  - Any C# language version is fine, but public API samples should use types compatible with .NET Standard 2.0 (i.e., avoid `record`, `required`, etc.)

---

## 1. v1 Scope (Locked)

### Target database
- SQL Server only
- Schema is fixed to **`dbo`**
  - In both `desired` and `current`, if the schema is omitted it is treated as `dbo`

### Supported objects / DDL (create/add only)
- `CREATE TABLE` (`dbo`)
  - Columns: type, NULL/NOT NULL, IDENTITY, DEFAULT (stored only)
  - Table constraints: PK / UNIQUE / CHECK / FK (add only)
- `ALTER TABLE ... ADD` (`dbo`)
  - Add columns (recommended: in v1, for safety, only nullable columns. Adding `NOT NULL` is treated as `-- Skipped:`)
  - Add constraints (PK/UQ/CHECK/FK)
- `CREATE INDEX` (`dbo`, including UNIQUE)

### Things v1 will never do (Locked)
- Alter existing definitions: `ALTER COLUMN`, type changes, nullability changes, DEFAULT changes, constraint definition changes, index redefinition, etc.
- Drop anything: `DROP TABLE/COLUMN/CONSTRAINT/INDEX`, REVOKE, etc.
- Rename support (the equivalent of `@renamed from=`)
- Silently ignore out-of-scope syntax

### Allowed statements in `desired` SQL (Locked)
- `desired` may contain **only**: `CREATE TABLE` / `ALTER TABLE ... ADD ...` / `CREATE INDEX`
- Anything else (e.g. `INSERT`, `DROP`, `ALTER COLUMN`, `EXEC`, `CREATE VIEW`) is an **immediate error**

---

## 2. Output Policy (B: Skipped Notifications)

If something exists in `current` but not in `desired` (a drop-equivalent), or a difference looks like an “alter”:
- **Do not generate any DDL**
- Always include `-- Skipped: <reason> <target>` in the output

Examples:
- `-- Skipped: drop is not supported in v1 dbo.Users.OldIndex`
- `-- Skipped: alter is not supported in v1 dbo.Users.Email DEFAULT differs`

`MigrationPlan` keeps `Operations` (executable DDL) and `Skipped` (notifications) separately.

---

## 3. Architecture Overview

### Inputs
- `desired`: SQL string (or multiple files in the future)
- `current`: target SQL Server database connection (`dbo`)

### Key components
- `DesiredSchemaLoader` (ScriptDom)
  - Split by GO batches → parse each batch via ScriptDom → build an in-memory model via a Visitor
- `CurrentSchemaReader` (sys catalog)
  - Read `dbo` metadata from `sys.*` and build the same in-memory model
- `SchemaDiffer`
  - Compare `current` and `desired` models and build an additive-only `MigrationPlan`
- `SqlServerSchemaApplier`
  - Execute `MigrationPlan` operations sequentially inside a transaction
- `MigrationPlan.ToScript()`
  - Render planned SQL and `-- Skipped:` items for review

---

## 4. Intermediate Model (Minimal)

Goal: normalize `desired` and `current` into the same shape so they can be compared. Since v1 never alters, the model contains only what is needed for diffing.

- `DatabaseModel`
  - `Tables: Dictionary<TableKey, TableModel>` (keyed by `dbo` + case-insensitive name)
- `TableModel`
  - `Schema = "dbo"`
  - `Name`
  - `Columns: Dictionary<ColumnKey, ColumnModel>`
  - `Constraints: Dictionary<ConstraintKey, ConstraintModel>` (PK/UQ/CK/FK)
  - `Indexes: Dictionary<IndexKey, IndexModel>`
- `ColumnModel`
  - `Name`
  - `SqlType` (string)
  - `IsNullable`
  - `IsIdentity`
  - `DefaultExpression` (string; compared as raw; differences become Skipped)
- `ConstraintModel`
  - `Kind: PK | UQ | CK | FK`
  - `Name`
  - Minimum per-kind details
    - PK/UQ: ordered columns
    - CK: `Definition` (raw; differences become Skipped)
    - FK: referenced table and ordered columns
- `IndexModel`
  - `Name`
  - `IsUnique`
  - `KeyColumns` (ordered)

Normalization (v1):
- Keys are case-insensitive (schema fixed to `dbo`)
- Expressions (DEFAULT/CHECK) are stored raw; differences generate `-- Skipped:` without emitting alter DDL

---

## 5. ScriptDom Parsing Design (`desired`)

### Parser
- Use a fixed ScriptDom parser version (e.g. `TSql160Parser`). Document the supported SQL Server version(s).

### GO (batch) handling
- ScriptDom does not support `GO`, so split into batches up front
- In v1, treat only `GO` at the start of a line (allowing surrounding whitespace) as a separator; ignore `GO` inside comments
- When applying, do not use `GO`; execute batches as sequential commands

### Visitor (allowed statements only)
- `CreateTableStatement`
- `AlterTableAddTableElementStatement` (ADD COLUMN / ADD CONSTRAINT)
- `CreateIndexStatement`
- Anything else is an **error**

### Rules to guarantee additive-only behavior
- Allow only `ALTER TABLE ... ADD ...` (any `ALTER COLUMN`, etc. is an immediate error)
- If an option cannot be interpreted safely in v1 (filtered index, INCLUDE, computed columns, special constraints, etc.), fail fast with an error to avoid incorrect diffs

---

## 6. sys Catalog Reading Design (`current`)

Read only `dbo` and map directly into the intermediate model (no DDL reconstruction).

Minimum sources:
- tables/schemas: `sys.tables`, `sys.schemas`
- columns/types: `sys.columns`, `sys.types`, `sys.identity_columns`
- defaults: `sys.default_constraints`
- PK/UQ: `sys.key_constraints`, `sys.indexes`, `sys.index_columns`
- CHECK: `sys.check_constraints`
- FK: `sys.foreign_keys`, `sys.foreign_key_columns`
- indexes: `sys.indexes`, `sys.index_columns`

---

## 7. Diff Generation (Additive Only) and Ordering

`SchemaDiffer.Diff(current, desired) -> MigrationPlan`

### Generated operations (add only)
- Since schema is fixed, `CREATE SCHEMA` is generally unnecessary in v1 (if it appears in `desired`, treating it as unsupported is acceptable)
- `CREATE TABLE dbo.X` (table missing in `current`)
- `ALTER TABLE dbo.X ADD <column>` (column missing in `current`)
- `ALTER TABLE dbo.X ADD CONSTRAINT ...` (PK/UQ/CK/FK missing in `current`)
- `CREATE [UNIQUE] INDEX ... ON dbo.X(...)` (index missing in `current`)

### Not generated (Skipped only)
- Exists in `current` but not in `desired` (drop-equivalent)
- Definition differences between `desired` and `current` (alter-equivalent)
  - DEFAULT expression differences, CHECK expression differences, column definition differences, constraint differences, index differences, etc.

### Apply order (safe)
1. `CREATE TABLE`
2. `ALTER TABLE ADD COLUMN`
3. `ALTER TABLE ADD CONSTRAINT` (PK/UQ/CK)
4. `CREATE INDEX`
5. `ALTER TABLE ADD CONSTRAINT` (FK)

---

## 8. DDL Rendering and Execution (dry-run / apply)

### Operation representation
- `SqlOperation { Description, Sql }`
- `SkippedItem { Reason, Target, Details? }`
- `MigrationPlan { Operations, Skipped, IsEmpty }`

### `ToScript` (for review)
- Render a consolidated list of `-- Skipped:` items (showing counts can be helpful)
- List each operation SQL (optionally with trailing `;`)

### Apply (execution)
- Execute sequentially via `Microsoft.Data.SqlClient` and `ExecuteNonQuery`
- Default: a single transaction
- On failure, throw an exception that includes “which operation failed” (Description/SQL)

### Logging
- Allow injecting `ILogger` for tracing during planning and apply

---

## 9. Test Strategy

### Unit tests (no DB)
- `desired` SQL → model (ScriptDom conversion)
- `current` model + `desired` model → plan (diff)
- Snapshot tests for `ToScript()` (order and Skipped wording)

Key cases:
- Create a new table
- Add a nullable column
- Add PK/UQ/CK/FK constraints (FK ordering)
- Extra objects exist in `current` → Skipped (drop-equivalent)
- DEFAULT/CHECK expression differences → Skipped (alter-equivalent)
- Unsupported statements mixed into `desired` → error

### Integration tests (Docker SQL Server)
- `Apply → Plan again` results in empty plan (idempotency)
- “Existing rows + adding a NOT NULL column” becomes Skipped (safety policy)

---

## 10. Packaging (Embedding First)

NuGet layout (suggested):
- `*.Core`
  - Intermediate model, diff, `MigrationPlan`, `ToScript`, skipped representations
- `*.SqlServer`
  - ScriptDom loader (`desired`)
  - sys catalog reader (`current`)
  - applier (apply/dry-run)
- (Optional) `*.Cli`
  - Debug CLI (makes adoption/verification faster)

Public API (minimal draft):
- `Task<MigrationPlan> ISchemaPlanner.PlanAsync(DbConnection, string desiredSql, PlannerOptions, CancellationToken)`
- `Task ISchemaApplier.ApplyAsync(DbConnection, MigrationPlan, ApplyOptions, CancellationToken)`
- `string MigrationPlan.ToScript(ScriptOptions options = null)`

---

## 11. Roadmap (Post v1 Ideas)

- v1.1: `@renamed from=` equivalent (rename assistance)
- v1.1: optionally allow safe `NOT NULL + DEFAULT` column additions
- v1.2: support alters (ALTER COLUMN, DEFAULT/CK changes, index redefinition)
- v2: drop the fixed-`dbo` limitation and support `TargetSchemas`, views/triggers, etc.

