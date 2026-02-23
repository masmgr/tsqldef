# tsqldef

A .NET CLI tool for declarative SQL Server schema management. Define your desired schema in a SQL file, and tsqldef computes the difference against a live database and generates (or applies) the migration script — idempotently and safely.

Inspired by [sqldef](https://github.com/sqldef/sqldef).

## Concepts

tsqldef follows a **declarative** approach to schema management:

1. You write a `desired.sql` file describing your intended schema (CREATE TABLE, CREATE INDEX, etc.)
2. tsqldef reads the current schema from a live SQL Server database
3. It computes the diff and produces an additive migration plan
4. You review the plan, then apply it

This is **safety-first** by design — v1 performs only additive changes (CREATE, ADD). Destructive operations (DROP, ALTER COLUMN) are skipped with warnings, or optionally handled via shadow-table rebuild proposals.

## Features

- **Export** current database schema to a SQL file
- **Plan** migration as a reviewable SQL script or JSON
- **Apply** migration to bring the database in sync with the desired schema
- **Strict mode** — fail if any schema differences cannot be resolved additively
- **Scope filters** — target specific tables with `--include` / `--exclude`
- **Shadow-table rebuild** — propose and execute table rebuilds for non-additive column changes (`--emit-swap-sql` / `--swap`)
- **Non-dbo schema support** via `--schema` option
- **JSON plan output** for CI/CD integration

### Supported DDL

| Category | Details |
|----------|---------|
| Tables | `CREATE TABLE`, `ALTER TABLE ADD COLUMN` |
| Column options | Data types, `NULL`/`NOT NULL`, `IDENTITY`, `DEFAULT`, `COLLATE` |
| Constraints | `PRIMARY KEY`, `UNIQUE`, `CHECK`, `FOREIGN KEY` (with `ON DELETE`/`ON UPDATE`) |
| Indexes | `CREATE [UNIQUE] [CLUSTERED\|NONCLUSTERED] INDEX`, `INCLUDE`, `WHERE` (filtered), `WITH` options |
| Extended properties | `MS_Description` on tables and columns |

### Not Supported (v1)

Computed columns, partitioning, columnstore/XML/spatial indexes, triggers, views, stored procedures, functions, row-level security, and masked columns are outside v1 scope and will be skipped.

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- SQL Server (any edition: Express, Developer, LocalDB, Azure SQL Database)

## Installation

```bash
dotnet build SqlSchemaDef.sln -c Release
```

The CLI binary is produced at `src/SqlSchemaDef.Cli/bin/Release/net8.0/SqlSchemaDef.Cli`.

## Usage

### Export the current schema

```bash
SqlSchemaDef.Cli export --connection "<connection-string>" --out desired.sql
```

Exports the live database schema to a `.sql` file. Omit `--out` to print to stdout.

### Plan a migration (dry run)

```bash
# SQL script output (default)
SqlSchemaDef.Cli plan --connection "<connection-string>" --file desired.sql

# JSON output for CI/CD
SqlSchemaDef.Cli plan --connection "<connection-string>" --file desired.sql --format json

# Strict mode — exit with code 30 if any items are skipped
SqlSchemaDef.Cli plan --connection "<connection-string>" --file desired.sql --strict

# Include rebuild proposals for non-additive column changes
SqlSchemaDef.Cli plan --connection "<connection-string>" --file desired.sql --emit-swap-sql
```

### Apply the migration

```bash
# Apply from desired SQL
SqlSchemaDef.Cli apply --connection "<connection-string>" --file desired.sql

# Apply from a saved JSON plan
SqlSchemaDef.Cli apply --connection "<connection-string>" --plan plan.json

# Execute shadow-table rebuilds for non-additive changes
SqlSchemaDef.Cli apply --connection "<connection-string>" --file desired.sql --swap
```

### Scope filters

```bash
# Only process specific tables
SqlSchemaDef.Cli plan --connection "<cs>" --file desired.sql --include Users,Orders

# Exclude specific tables
SqlSchemaDef.Cli plan --connection "<cs>" --file desired.sql --exclude Logs,Audit
```

### Non-dbo schemas

```bash
SqlSchemaDef.Cli plan --connection "<cs>" --file desired.sql --schema sales
```

When `--schema` is specified, schema-less DDL (e.g., `CREATE TABLE T ...`) is treated as belonging to that schema. DDL with an explicit schema that doesn't match will be rejected with exit code 11.

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | Usage error (invalid arguments) |
| 10 | Desired SQL parse error |
| 11 | Unsupported statement or schema mismatch |
| 20 | Apply failed |
| 30 | Strict mode violation (skipped items detected) |

## Project Structure

| Project | Target | Role |
|---------|--------|------|
| `SqlSchemaDef.Core` | netstandard2.0 | Domain types, interfaces, plan serialization |
| `SqlSchemaDef.SqlServer` | netstandard2.0 | SQL Server implementation (ScriptDom + SqlClient) |
| `SqlSchemaDef.Cli` | net8.0 | CLI entry point |
| `SqlSchemaDef.Tests` | net8.0 | xUnit + FsCheck property-based tests |

## Building and Testing

```bash
# Build
dotnet build SqlSchemaDef.sln

# Run unit tests (no SQL Server required)
dotnet test SqlSchemaDef.sln --filter "Category!=Integration"

# Run all tests including integration (requires SQL Server)
dotnet test SqlSchemaDef.sln
```

Integration tests auto-detect LocalDB on Windows. To use a different SQL Server instance, set `SQLSCHEMADEF_TEST_CONNECTION_STRING`.

## Design Documents

Detailed design documents are available in `docs/README.md`.
