# dotnet-sqldef v1 Implementation Task List (TDD)

Goal: a work checklist for implementing v1 (SQL Server / fixed `dbo` / additive-only) from `docs/dotnet-sqldef-plan.md` using **test-driven development (Red → Green → Refactor)**.

Assumptions:
- Separate `SqlSchemaDef.Core` (public API + plan representation) and `SqlSchemaDef.SqlServer` (SQL Server implementation)
- Additive-only (never emit destructive DDL)
- If `desired` includes any disallowed DDL, fail fast (error)

---

## How to proceed (TDD rules)

- 1 PR (or 1 commit) = one small behavior
- For every task, **add tests first** (Red), then implement (Green), then clean up (Refactor)
- Definition of Done:
  - [ ] The added test fails in the intended way (confirm Red)
  - [ ] Tests pass after implementation (Green)
  - [ ] `dotnet test SqlSchemaDef.sln -c Release` passes
  - [ ] If the public API changes, update `docs/dotnet-sqldef-api-design.md` (when needed)

---

## 0. Repo / scaffolding (Done)

- [x] Create solution: `SqlSchemaDef.sln` / `SqlSchemaDef.slnx`
- [x] Create project layout: `src/SqlSchemaDef.Core` (`netstandard2.0`)
- [x] Create project layout: `src/SqlSchemaDef.SqlServer` (`netstandard2.0`)
- [x] Create project layout: `src/SqlSchemaDef.Cli` (`net8.0`)
- [x] Create project layout: `tests/SqlSchemaDef.Tests` (xUnit, `net8.0`)
- [x] References: `Cli -> SqlServer -> Core` / `Tests -> Core`
- [x] Add Core public API skeleton (plan/options/exceptions/ToScript)
- [x] Add SqlServer planner/applier skeleton (Plan currently returns empty)
- [x] Add unit tests for `MigrationPlan.ToScript()`

---

## 1. `desired`: GO splitting (`BatchSplitter`)

Reference: `docs/dotnet-sqldef-scriptdom-visitor-spec.md` (GO splitting spec)

- [x] Test: basic `GO` splitting (case-insensitive, surrounding whitespace)
- [x] Test: `-- GO` / `/* GO */` do not split
- [x] Test: empty final batch is not an error (skip empties)
- [x] Test: `GO 2` throws `UnsupportedBatchSeparatorException` (with line number)
- [x] Implement: `BatchSplitter` in `SqlSchemaDef.SqlServer` (`BatchIndex`/`StartLine`/`Text`)
- [x] Refactor: deduplicate split logic and line correction

---

## 2. `desired`: ScriptDom parsing (`DesiredSqlParser`)

References:
- `docs/dotnet-sqldef-scriptdom-visitor-spec.md` (parser policy / exception templates)

- [x] Test: ScriptDom parse errors become `DesiredSqlParseException` (all `Diagnostics`)
- [x] Test: `Diagnostics` includes `BatchIndex`/`Line`/`Column` (with `StartLine` correction)
- [x] Implement: `DesiredSqlParser` (fix `TSql160Parser`, `initialQuotedIdentifiers: true`, etc.)
- [x] Implement: correct parse error `Line` by adding `StartLine`

---

## 3. `desired`: Visitor (allowed DDL only)

Reference:
- `docs/dotnet-sqldef-scriptdom-visitor-spec.md` (accepted DDL / unsupported features / exceptions)

### 3.1 Unsupported statements (immediate error)
- [x] Test: e.g. `CREATE VIEW` throws `UnsupportedDesiredStatementException`
- [x] Test: e.g. `ALTER TABLE ... ALTER COLUMN` throws `UnsupportedDesiredStatementException`
- [x] Implement: throw using template (A) (include batch/line/column)

### 3.2 Fixed `dbo` violations (immediate error)
- [x] Test: `CREATE TABLE foo.X (...)` throws `UnsupportedSchemaException`
- [x] Test: FK referencing a schema other than `dbo` throws `UnsupportedSchemaException`
- [x] Implement: schema resolution and `dbo`-only validation

### 3.3 Unsupported features within allowed DDL (immediate error)
- [x] Test: `CREATE INDEX ... INCLUDE (...)` throws `UnsupportedDesiredFeatureException`
- [x] Test: filtered index (`WHERE ...`) throws `UnsupportedDesiredFeatureException`
- [x] Test: index `WITH (...)` / `ONLINE` throws `UnsupportedDesiredFeatureException`
- [x] Test: computed columns throw `UnsupportedDesiredFeatureException`
- [x] Implement: populate `FeatureName`/`StatementType` and throw

### 3.4 Model building for CreateTable / AlterTableAdd / CreateIndex
- [x] Test: `CREATE TABLE dbo.T (...)` becomes part of the `desired` model
- [x] Test: `ALTER TABLE dbo.T ADD Col int NULL` becomes part of the `desired` model
- [x] Test: `ALTER TABLE dbo.T ADD Col int NOT NULL` defaults to skipped (`NotNullAddNotSupported`) (align with policy)
- [x] Test: `CREATE UNIQUE INDEX ...` becomes part of the `desired` model
- [x] Implement: visitor + minimal intermediate model (only what v1 needs)

---

## 4. `current`: sys catalog reading (`CurrentSchemaReader`)

Reference: `docs/dotnet-sqldef-syscatalog-queries.md`

- [x] Test (integration or low-level): map table/column query results into the model
- [x] Test (unit): type stringification (e.g. `nvarchar(max)`, `decimal(p,s)`)
- [x] Implement: sys queries in code (fixed `dbo`)
- [x] Implement: type stringification utility
- [x] Implement: detect and handle v1-unsupported elements in `current` (computed/INCLUDE/filtered/descending, etc.) (policy: prefer skipped)

---

## 5. Diff (additive-only) → `MigrationPlan`

References:
- `docs/dotnet-sqldef-plan.md` (v1 diff policy)
- `docs/dotnet-sqldef-test-plan.md` (ordering/skipped)

### 5.1 Additive-only operations
- [x] Test: if missing in `current`, emit `CREATE TABLE`
- [x] Test: if missing in `current`, emit `ALTER TABLE ADD COLUMN` (nullable only)
- [x] Test: if missing in `current`, emit PK/UQ/CK
- [x] Test: if missing in `current`, emit INDEX
- [x] Test: FK is emitted after referenced tables are created (ordering)
- [x] Implement: `SchemaDiffer` (generate operations)

### 5.2 Alter/drop become skipped
- [x] Test: exists only in `current` (drop-equivalent) becomes `SkippedReason.DropNotSupported`
- [x] Test: definition differences (alter-equivalent) become `SkippedReason.AlterNotSupported`
- [x] Test: v1-unsupported elements in `current` do not become operations and are collected as skipped (align with policy)
- [x] Implement: collect `SkippedItem` (lock down `Target`/`Message` conventions)

### 5.3 Determinism (ordering + tie-breaking by name)
- [x] Test: for the same input, `Operations`/`Skipped` order is always identical
- [x] Implement: sort by `OperationKind` + name

---

## 6. `ToScript` (review output)

Reference:
- `docs/dotnet-sqldef-test-plan.md` (`ToScript` verification)

- [x] Test: header output for an empty plan
- [x] Test: formatting for operations + skipped
- [x] Test: `TerminateWithSemicolon=false` output
- [x] Test: `IncludeSkipped=false` output
- [x] Test: newlines follow `ScriptOptions.NewLine` (default `\n` for stable snapshots)
- [x] Implement: finalize `ToScript()` spec (wording/order)

---

## 7. Apply (SQL execution)

Reference:
- `docs/dotnet-sqldef-api-design.md` (`ApplyFailedException` contract)

- [x] Test (integration): execute operations in order and make the DB converge toward `desired`
- [x] Test (integration): on failure, `ApplyFailedException.Operation` is usable
- [x] Test (integration): `TransactionMode=SingleTransaction` runs in one transaction (rollback on failure)
- [x] Implement: lock down `SqlServerSchemaApplier` behavior (logging/exceptions/cancellation)

---

## 8. CLI (minimal → ops-friendly)

- [x] Smoke test: `--help` prints usage
- [x] Smoke test: dry-run prints `ToScript()` (exit code 0)
- [x] Smoke test (optional): `--apply` runs Apply
- [x] Implement: exit code conventions (parse/unsupported/apply failure)
- [x] Implement: decide policy for `--schema` (v1 is fixed `dbo`; can be hidden/unimplemented for future)

---

## 9. Integration tests (Docker SQL Server)

Reference: `docs/dotnet-sqldef-test-plan.md`

- [x] Test infra: start Docker SQL Server (inject connection string via env var)
- [x] Test infra: 1 test = 1 DB (unique DB name; avoid parallel collisions)
- [x] Test: idempotency (empty DB → Plan/Apply → Plan again yields `IsEmpty == true`) for 5–10 patterns
- [x] Test: extra objects in `current` do not produce DROP (prefer skipped)
- [x] Test: “existing rows + NOT NULL column add” becomes skipped

---

## 10. CI / quality

- [x] CI: always run unit tests (`dotnet test`)
- [x] CI: integration tests start Docker service + print logs on failure
- [x] Docs: if specs change, update related docs (plan/api/spec/test plan) accordingly

---

## 11. v0.1 Product milestone: export / plan / apply (tables + columns + indexes)

Goal: make the existing v1 engine easy to use from the CLI and add an `export` command to bootstrap `desired.sql`.

### 11.1 CLI UX: subcommands + file IO
- [ ] Test: `--help` shows `export|plan|apply` and examples
- [ ] Implement: `SqlSchemaDef.Cli` subcommands
  - `export --connection ... [--out desired.sql]`
  - `plan --connection ... --file desired.sql [--format script|json]`
  - `apply --connection ... (--file desired.sql | --plan plan.json)`

### 11.2 Export: current schema → desired.sql (minimum set)
- [ ] Test (integration): `export` then `plan` returns `IsEmpty == true` for the same DB (or only expected `Skipped`)
- [ ] Implement: `SqlServerSchemaExporter` (or equivalent) to render stable DDL for:
  - `CREATE TABLE` with columns (types/nullability/identity/default as stored)
  - `CREATE [UNIQUE] INDEX` (v1-supported subset only)
- [ ] Decide: what to do for unsupported `current` features (prefer omit + `-- Skipped:` style notes in export output)

---

## 12. v0.2 Product milestone: constraints + foreign keys (additive-only)

Goal: ship additive-only constraint/FK creation with a safe apply order.

Status note:
- Most v0.2 capabilities correspond to the already-implemented v1 engine tasks in sections `3`/`4`/`5`/`7`/`9` above.
- v1 treats DEFAULT differences as non-additive (Skipped); v0.2 does not expand that policy.

- [ ] Verify via integration tests: missing PK/UQ/CK/FK converge after apply, then `Plan` becomes empty
- [ ] Add (unit) tests for any remaining edge cases: composite keys ordering, multi-column FKs, determinism across batches

---

## 13. v0.3 Product milestone: dangerous operation blocking + options + JSON plan

Goal: harden safety and enable machine-readable plans for CI/CD workflows.

### 13.1 Safety rail: strict mode / exit codes
- [ ] Test: `plan --strict` exits non-zero if any `Skipped` items exist
- [ ] Implement: CLI `--strict` option and exit code convention
- [ ] Test: `apply` refuses to run if the plan contains any non-additive operations (should be impossible, but validate defensively)

### 13.2 JSON plan format
- [ ] Decide: JSON schema versioning policy (e.g. `PlanFormatVersion = 1` in `PlanMetadata`)
- [ ] Test: `plan --format json` produces deterministic JSON (stable ordering)
- [ ] Test: `apply --plan plan.json` applies the same operations as script mode
- [ ] Implement: JSON serialization/deserialization of `MigrationPlan` (Core types) with a stable contract

### 13.3 Scope filters
- [ ] Test: `--include/--exclude` filters tables for plan generation (does not change parsing rules)
- [ ] Implement: `PlannerOptions` extensions + CLI wiring (v1 is fixed `dbo`, but table filtering is still useful)

---

## 14. v0.4 Product milestone: rebuild proposal (swap SQL only)

Goal: for non-additive diffs, generate actionable “manual migration” SQL without executing it.

- [ ] Decide: proposal representation (`RebuildProposal` list in `MigrationPlan.Metadata` or a separate section)
- [ ] Test: type change diff produces a proposal with shadow table + copy + swap steps
- [ ] Implement: proposal generator (no apply support)
- [ ] CLI: `plan --emit-swap-sql` prints proposals after the normal v1 plan output
