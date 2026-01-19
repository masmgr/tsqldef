# v1 Test Plan (SQL Server only / fixed `dbo` / additive-only)

This document describes a test plan to ensure quality for the v1 contract (fixed `dbo`, additive-only, alter/drop become `-- Skipped:`, and `desired` fails fast if it contains unsupported DDL).

Targets:
- The library targets **.NET Standard 2.0**
- The test project can target anything (e.g. `net8.0`), but public APIs of the referenced library are assumed to be .NET Standard 2.0 compatible

---

## 1. Test goals (v1)

- **Correctness**: generate correct additive-only diff DDL
- **Safety**: reliably stop (error) or avoid execution (Skipped) when alter/drop/unsupported DDL is involved
- **Idempotency**: `Apply -> Plan again` results in `IsEmpty == true`
- **Determinism**: the same input always yields the same `Operations` order and `ToScript()` output
- **Debuggability**: errors report “what” and “where” (batch/line/column)

---

## 2. Test layers (recommended)

1) **Unit (no DB)**
- Validate GO splitting, AST→model, model→plan, `ToScript`, and exception messages

2) **Integration (Docker SQL Server)**
- Read sys catalog → model, round-trip Plan/Apply, validate idempotency

3) (Optional) **Smoke (if a CLI exists)**
- End-to-end exercise of the execution path (a light CI final step)

---

## 3. Test environment

### 3.1 .NET test runner
- xUnit (recommended) or NUnit/MSTest (any is fine)
- Snapshot testing: Verify.Xunit, etc. (golden output for `ToScript()`)

### 3.2 SQL Server (integration tests)
- Docker: `mcr.microsoft.com/mssql/server` (e.g. `2019-latest`)
- Connection: `Microsoft.Data.SqlClient`
- Use a unique DB name per test (avoid collisions under parallel runs)
- Recommend 1 test = 1 database (strong isolation)

---

## 4. Test categories and coverage

### 4.1 ScriptDom: GO splitting
Purpose:
- Correctly split on `GO`, ignore `GO` inside comments, error on `GO 123`, and correct line number adjustments

Key cases:
- `GO` (case-insensitive, surrounding whitespace)
- `-- GO` / `/* GO */` do not split
- `GO 2` results in `Unsupported batch separator` error
- An empty final batch is not an error (skip empties)

Assertions:
- Batch count, each batch text, `StartLine` values

### 4.2 `desired` parsing: allowed/disallowed statements
Purpose:
- Accept only statements allowed in v1

Allowed:
- `CREATE TABLE`
- `ALTER TABLE ... ADD ...` (columns/constraints)
- `CREATE INDEX` / `CREATE UNIQUE INDEX`

Disallowed examples:
- `DROP`, `ALTER COLUMN`, `CREATE VIEW`, `INSERT`, `EXEC`, `MERGE`, `CREATE SCHEMA`, `CREATE TRIGGER`

Assertions:
- Exception type and message (matches `Unsupported desired statement in v1...`)
- If possible, include batch/line/column

### 4.3 `desired` feature restrictions (unsupported features within allowed statements)
Purpose:
- Ensure unsupported features in v1 become errors (or Skipped if that is the v1 rule)

Key cases (error recommended):
- `CREATE INDEX ... INCLUDE (...)`
- filtered index: `WHERE ...`
- index `WITH (...)` / `ONLINE`
- computed columns
- schema references other than `dbo` (table names or referenced targets)
- FK `ON UPDATE/DELETE` (if not supported in v1, error)
- unnamed constraints (if treated as unsupported in v1)

Assertions:
- Matches `Unsupported desired feature in v1...` / `Unsupported schema in v1...` templates

### 4.4 Model → Plan (additive-only)
Purpose:
- Produce additive-only operations and Skipped items from a `current` vs `desired` diff

Coverage:
- New tables: `CREATE TABLE`
- Added columns: `ALTER TABLE ADD COLUMN` (nullable only)
- Added constraints: `ALTER TABLE ADD CONSTRAINT` (PK/UQ/CK/FK)
- Added indexes: `CREATE INDEX`
- Alter-equivalent diffs (type/null/default/check definition differences) do not appear in operations (they become skipped)
- Drop-equivalent diffs (`current` only) do not appear in operations (they become skipped)

Assertions:
- `Operations` count and contents (`Description`/`Kind`/`Target`)
- `Skipped` count and `Reason`

### 4.5 DDL order (determinism + dependency safety)
Purpose:
- Operations are ordered safely and deterministically

Order requirements (v1):
1. CreateTable
2. AddColumn
3. AddConstraint (PK/UQ/CK)
4. CreateIndex
5. AddForeignKey

Cases:
- FK referencing a table also created in the same `desired` (must be created first)
- Multiple tables and multiple FKs maintain stable ordering (ties resolved by name)

Assertions:
- `OperationKind` order
- `ToScript()` output order is stable

### 4.6 `ToScript` (review output)
Purpose:
- `-- Skipped:` rendering follows spec
- Options like trailing `;` work

Cases:
- operations 0 / skipped > 0
- operations > 0 / skipped 0
- both present

Assertions:
- Snapshot tests (golden files) for easy diff review

---

## 5. Integration tests (with DB)

### 5.1 sys catalog → model
Purpose:
- The SELECT set in `dotnet-sqldef-syscatalog-queries.md` builds the expected `current` model

Cases:
- A simple table (int/varchar/nvarchar/decimal/datetime2, etc.)
- IDENTITY column
- DEFAULT constraint
- PK/UQ/CK/FK constraints
- Indexes (excluding PK/UQ)

Assertions:
- Each element exists in the `current` model with correct ordering/definitions

### 5.2 Idempotency (most important)
Steps:
1. Create an empty DB
2. Plan → Apply for a `desired`
3. Plan again with the same `desired`

Expected:
- The second plan has `IsEmpty == true`
- Ideally `Skipped` is 0, but it may exist if v1-unsupported elements exist in `current`

### 5.3 Additive-only safety
Case:
- `current` has extra tables/columns/constraints/indexes

Expected:
- No DROP operations are emitted
- `SkippedReason.DropNotSupported` is produced

### 5.4 Safety policy for adding NOT NULL columns
Case:
- Add a `NOT NULL` column to a table with existing rows

Expected:
- No operation is emitted
- `SkippedReason.NotNullAddNotSupported` is produced

### 5.5 v1-unsupported elements in `current`
Examples:
- Computed columns / INCLUDE indexes / filtered indexes / descending keys exist in `current`

Expected:
- v1 never emits alter/drop, so no operations are generated
- If `desired` does not include them, treat as drop-equivalent → `SkippedReason.DropNotSupported`
- If `desired` includes an object with the same name but a differing definition, treat as alter-equivalent → `SkippedReason.AlterNotSupported`

---

## 6. Test data design (recommended)

### 6.1 `desired` SQL templates
- Create `desired/*.sql` and load them in tests (readability)
- Use dedicated files for GO/comment-mixing cases

### 6.2 Creating `current`
- In integration tests, execute “current setup SQL” to build the state (validates sys-reading)
- Keep current DDL as standard as possible within what SQL Server accepts

---

## 7. Non-functional tests (minimum for v1)

- **Cancellation**: `ISchemaPlanner.PlanAsync` / `ISchemaApplier.ApplyAsync` honor `CancellationToken` (use a large `desired`)
- **Determinism**: run the same input N times and ensure `ToScript()` is identical (unit test is fine)

Performance checks in v1 can be light (ensure no obvious regressions). If needed, add benchmarks separately.

---

## 8. CI execution plan (recommended)

- `dotnet test` (unit): always
- `dotnet test` (integration):
  - Start Docker SQL Server as a service
  - Inject connection string via env var
  - On failure, print container logs

Optional matrix:
- SQL Server: start with `2019-latest`, then add `2022/2025` once stable
- OS: Linux in CI by default; Windows can be added later

---

## 9. Acceptance criteria (v1 done)

- Unit: key cases covered (allowed/disallowed, unsupported features, ordering, `ToScript`, skipped)
- Integration: at least 5–10 patterns validate “empty DB → Apply → Plan again is empty”
- Exception messages: include batch index/line/column (at least for parse errors and unsupported statements)
- Additive-only: tests ensure no alter/drop DDL appears in operations

