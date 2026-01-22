# v1 ScriptDom Visitor: Supported Statements and Conversion Spec (fixed `dbo` / additive-only)

Based on the v1 principles in `dotnet-sqldef-plan.md` (fixed `dbo`, additive-only, alter/drop become `-- Skipped:`, and `desired` fails fast if it contains unsupported DDL), this document locks down:
- the list of statements accepted by the ScriptDom visitor,
- conversion rules into the intermediate model, and
- exception message conventions.

Scope:
- `desired` SQL (input DDL) only (`current` is read from `sys.*`)
- ScriptDom: `Microsoft.SqlServer.TransactSql.ScriptDom`

---

## 1. Preprocessing: GO Batch Splitting (Required)

### 1.1 Purpose
ScriptDom does not treat `GO` as a grammar element, so input separated by `GO` must be split into batches before parsing.

### 1.2 v1 splitting rules (Locked)
- Only **`GO` at the start of a line** is a separator (allowing surrounding whitespace)
- Case-insensitive (`go`, `Go` also split)
- `GO` inside line comments (`-- ...`) or block comments (`/* ... */`) is **not** a separator
- `GO 123` (repeat count) is **unsupported in v1** (error)

### 1.3 Line/column tracking during splitting
To report “line/column in the original SQL” in diagnostics/exceptions, keep this per batch:
- `BatchIndex` (0-based)
- `StartLine` (1-based start line in the original SQL)
- `Text` (batch text)

`TSqlParser.Parse(...)` reports `ParseError.Line` relative to the batch, so add `StartLine` when rendering messages.

---

## 2. Parser Version Policy

In v1, fix the parser version and document the supported SQL Server version (e.g., SQL Server 2019+).

Recommended:
- `TSql160Parser` (roughly SQL Server 2022) or `TSql170Parser` (roughly SQL Server 2025)
- `initialQuotedIdentifiers: true`

---

## 3. Visitor Responsibilities (v1)

### 3.1 Allowed top-level statements
v1 accepts only:
- `CreateTableStatement`
- `AlterTableAddTableElementStatement`
- `CreateIndexStatement`

Anything else is an **immediate error** (to reliably stop when destructive/unsupported DDL is mixed in).

### 3.2 Guaranteeing “additive-only”
Even within allowed statements, the following are prohibited in v1:
- Anything other than `ADD` in `ALTER TABLE` (`AlterTableAlterColumnStatement`, etc.) (also disallowed by top-level rules)
- `CREATE INDEX` with `WHERE` (filtered) / `WITH(...)` / `ONLINE`, etc. (error)
- Computed columns, columnsets, compression, partitioning, special index types, etc. (error)

---

## 4. Conversion to the Intermediate Model (Locked)

The model concept must match the “Intermediate Model (Minimal)” section of `dotnet-sqldef-plan.md`.

### 4.1 Common rules: identifiers and fixed `dbo`
- Schema name:
  - If a table name omits schema, treat it as `dbo`
  - If a schema is specified and it is not `dbo`, it is unsupported in v1 → error
- Comparison keys are case-insensitive, but **rendering uses the `desired` casing** when possible

#### Implementation notes (recommended)
- `NormalizeSchema(Identifier)`:
  - null/empty → `"dbo"`
  - `"dbo"` (case-insensitive) → `"dbo"`
  - anything else → error
- `NormalizeNameKey(string)`:
  - Normalize (e.g. `ToUpperInvariant()`) to build dictionary keys

### 4.2 `CreateTableStatement` → `TableModel`

Accepted:
- `CREATE TABLE dbo.X (...)`

Rejected (error):
- `CREATE TABLE ... AS SELECT`, temp tables, external tables, system-table features
- `WITH (...)` options (storage/compression/partition/filegroup) unsupported in v1
- Inline `INDEX` definitions inside `CREATE TABLE` (depends on ScriptDom representation, but treat as unsupported in v1)

#### 4.2.1 Columns (`ColumnDefinition`)
Mapping:
- `ColumnModel.Name`
- `ColumnModel.IsNullable`
- `ColumnModel.IsIdentity` (presence of `IDENTITY`)
- `ColumnModel.SqlType` (stringified using the rules below)
- `ColumnModel.DefaultExpression` (raw string if `DEFAULT` exists)

Type stringification (recommended for v1):
- Format a minimal canonical representation from ScriptDom `DataTypeReference`
  - Examples: `nvarchar(255)` / `nvarchar(max)` / `decimal(18,2)` / `datetime2(7)`
- Use this value as a hint for comparison; if it differs, v1 still does not emit alter DDL (it becomes `-- Skipped:`)

Computed columns are unsupported in v1:
- If `ColumnDefinition.ComputedColumnExpression != null`, throw an error

NOT NULL:
- `NOT NULL` in `CREATE TABLE` is allowed (it is a new table, so it is safe)
- `NOT NULL` in `ALTER TABLE ... ADD` is skipped by default in v1 (see below)

#### 4.2.2 Table constraints (`TableDefinition` / `ConstraintDefinition`)
Supported kinds:
- PRIMARY KEY
- UNIQUE
- CHECK
- FOREIGN KEY

Constraint names:
- If the name is omitted, treat it as unsupported in v1 (recommended) and throw

PRIMARY KEY / UNIQUE:
- `ConstraintModel.Kind = PK|UQ`
- `ConstraintModel.Name`
- Ordered key columns
- Attributes such as clustered/nonclustered are unsupported in v1 → error (or ignore; but “fail fast” is recommended in v1)

CHECK:
- `ConstraintModel.Kind = CK`
- `ConstraintModel.Name`
- `ConstraintModel.Definition = <expression raw string>`
  - Use ScriptDom script generation or fragment text to preserve raw expression

FOREIGN KEY:
- `ConstraintModel.Kind = FK`
- `ConstraintModel.Name`
- Parent table (fixed `dbo`)
- Parent columns (ordered)
- Referenced table/columns (fixed `dbo`)
- `ON DELETE/UPDATE` is not handled in v1; if present, error is recommended

### 4.3 `AlterTableAddTableElementStatement` → Additions

Accepted:
- `ALTER TABLE dbo.X ADD <column>`
- `ALTER TABLE dbo.X ADD CONSTRAINT ...` (PK/UQ/CK/FK)

Rejected (error):
- `ALTER TABLE` with `ALTER COLUMN`/`DROP`/`WITH CHECK`, etc. (also disallowed by top-level rules)

Add column:
- Resolve table name (fixed `dbo`)
- Convert `ColumnDefinition` into `ColumnModel`
- v1 safety policy:
  - Adding a `NOT NULL` column defaults to **Skipped (not error)** because it often fails when the table has existing rows
  - Even with a `DEFAULT`, v1 does not guarantee “safe NOT NULL additions”, so default remains skipped
  - Set skipped `Reason = NotNullAddNotSupported`

Add constraint:
- Same conversion rules as for `CREATE TABLE`
- If `ADD CONSTRAINT` omits a name, treat it as unsupported in v1 (recommended) and throw

### 4.4 `CreateIndexStatement` → `IndexModel`

Accepted:
- `CREATE INDEX IX ... ON dbo.T(col1, col2)`
- `CREATE UNIQUE INDEX ...`
- `CREATE INDEX ... ON dbo.T(col1 DESC, col2 ASC)` (explicit sort order)
- `CREATE INDEX ... INCLUDE (col3, col4)`

Rejected (error):
- `WHERE ...` (filtered index)
- `WITH (...)` (fillfactor/online, etc.)
- `ON <filegroup/partition scheme>`, etc.

Mapping:
- `IndexModel.Name`
- `IndexModel.IsUnique`
- `IndexModel.KeyColumns` (ordered)
- `IndexModel.IncludeColumns` (ordered)
- Key column sort order (`ASC` / `DESC`) is preserved per column

---

## 5. Exceptions (Message Conventions Locked)

### 5.1 Exception types (recommended)
- `DesiredSqlParseException` (ScriptDom parse errors)
- `UnsupportedDesiredStatementException` (statement type not allowed)
- `UnsupportedDesiredFeatureException` (unsupported feature within an allowed statement)

### 5.2 Required message elements
All messages should include:
- Error category (fixed wording)
- Batch index (0-based)
- Line/column in the original SQL (1-based, if possible)
- Statement type (if available)
- A hint that v1 is additive-only

### 5.3 Message templates (Locked)

#### (A) Unsupported statement
```
Unsupported desired statement in v1 (additive-only).
Only CREATE TABLE / ALTER TABLE ... ADD ... / CREATE INDEX are supported.
Found: {StatementType} at batch {BatchIndex}, line {Line}, column {Column}.
```

#### (B) Unsupported feature (e.g. CREATE INDEX WHERE)
```
Unsupported desired feature in v1 (additive-only).
Feature: {FeatureName}. Statement: {StatementType}.
Location: batch {BatchIndex}, line {Line}, column {Column}.
```

#### (C) Schema not supported (non-dbo)
```
Unsupported schema in v1.
Only schema 'dbo' is supported. Found: '{SchemaName}'.
Location: batch {BatchIndex}, line {Line}, column {Column}.
```

#### (D) GO repeat count
```
Unsupported batch separator in v1.
"GO {N}" is not supported; use plain "GO".
Location: line {Line}.
```

### 5.4 Collecting ScriptDom parse errors
Collect all `IList<ParseError>` items from `TSqlParser.Parse` and store them into `DesiredSqlParseException.Diagnostics`.

Recommended formatting:
```
Failed to parse desired SQL.
Batch {BatchIndex}, line {Line}, column {Column}: {Message}
```

---

## 6. Visitor Implementation Guide (Minimum)

### 6.1 Suggested class split
- `BatchSplitter` (GO splitting + `StartLine` tracking)
- `DesiredSqlParser`
  - `ParseBatches(IEnumerable<SqlBatch>) -> IReadOnlyList<TSqlFragment>`
  - Parse error line correction (add `StartLine`)
- `DesiredModelBuilderVisitor : TSqlFragmentVisitor`
  - `Visit(CreateTableStatement)`
  - `Visit(AlterTableAddTableElementStatement)`
  - `Visit(CreateIndexStatement)`
  - For anything else, throw from `ExplicitVisit(TSqlStatement)`

### 6.2 “Everything else throws” approach
Enumerate ScriptDom statements and throw immediately when encountering anything not in the allowed set.

Recommended: override `ExplicitVisit(TSqlStatement node)` and only handle allowed statement types via dedicated overrides; for all others, throw using template (A).

---

## 7. Explicit v1 Trade-offs

- Do not allow unnamed constraints (recommended)
  - Helps keep idempotency simple in v1
  - Future versions can relax this by emulating SQL Server’s auto-naming rules
- Keep DEFAULT/CHECK expressions as raw strings
  - Since v1 does not emit alter DDL, do not normalize them
- Advanced index features (filtered/with/online/etc.) are unsupported in v1
