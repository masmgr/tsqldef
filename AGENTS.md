# Agent Guide (tsqldef / dotnet-sqldef)

このリポジトリは、SQL Server 向けの **additive-only (v1)** スキーマ差分ツールです。
`desired` SQL と `current` DB スキーマを比較し、
- 実行可能な追加系 DDL (`Operations`)
- v1 では実行しない差分 (`Skipped`)
を含む `MigrationPlan` を生成します。

## TL;DR

- まず `dotnet build SqlSchemaDef.sln -c Release`、次に `dotnet test SqlSchemaDef.sln -c Release`。
- v1 は **破壊的変更なし**（DROP/ALTER は実行しない）。
- `Core` / `SqlServer` は `netstandard2.0` ターゲット（`net8.0` 専用 API を入れない）。
- 許容 SQL や挙動を変える場合は `docs/` を同時更新。

## Repository Map

- `src/SqlSchemaDef.Core/`
  - DB 非依存の契約・モデル・シリアライズ。
  - 主要: `MigrationPlan`, `SqlOperation`, `SkippedItem`, `PlanValidator`, `MigrationPlanSerializer`。

- `src/SqlSchemaDef.SqlServer/`
  - SQL Server 実装。
  - Parsing: `BatchSplitter`, `DesiredSqlParser`, `DesiredSchemaLoader`
  - Reading: `CurrentSchemaCatalogReader`, `CurrentSchemaModelBuilder`, `CurrentSchemaReader`
  - Diffing: `SchemaDiffer`, `SqlStatementBuilder`, `RebuildProposalBuilder`
  - Entrypoints: `SqlServerSchemaPlanner`, `SqlServerSchemaApplier`, `SqlServerSchemaExporter`

- `src/SqlSchemaDef.Cli/`
  - `export` / `plan` / `apply` の最小 CLI。

- `tests/SqlSchemaDef.Tests/`
  - Unit / Integration / Property(FsCheck) テスト。

- `docs/`
  - 設計ドキュメント。
  - 入口: `docs/README.md`

## Build / Test / Run

- Restore: `dotnet restore SqlSchemaDef.sln`
- Build (CI 相当): `dotnet build SqlSchemaDef.sln -c Release`
- Test: `dotnet test SqlSchemaDef.sln -c Release`
- CLI help: `dotnet run --project src/SqlSchemaDef.Cli -- --help`

### Coverage

- PowerShell: `./coverage.ps1`
- bash: `./coverage.sh`

## CLI Contract (Current)

- `export --connection <cs> [--out <desired.sql>]`
- `plan --connection <cs> --file <desired.sql> [--format script|json] [--strict] [--emit-swap-sql] [--include <csv>] [--exclude <csv>]`
- `apply --connection <cs> (--file <desired.sql> | --plan <plan.json>) [--include <csv>] [--exclude <csv>]`

Exit code:
- `0`: success
- `2`: usage error
- `10`: desired SQL parse error
- `11`: unsupported desired statement/feature/schema/batch separator
- `20`: apply failed
- `30`: strict mode violation (Skipped が 1 件以上)

## Integration Tests (SQL Server)

Integration 系テストは `SqlServerTestDatabase.GetMasterConnectionStringOrNull()` が `null` の場合に早期 return します。
接続解決順は以下です。

1. `SQLSCHEMADEF_TEST_CONNECTION_STRING`
2. Local fallback
   - `(localdb)\MSSQLLocalDB`
   - `.\SQLEXPRESS`

`SQLSCHEMADEF_TEST_CONNECTION_STRING` を使う場合は `Initial Catalog=master` 推奨。

例 (Docker):
- `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Your_password123 -p 1433:1433 --name tsqldef-sql --rm mcr.microsoft.com/mssql/server:2022-latest`

例 (PowerShell):
- `$env:SQLSCHEMADEF_TEST_CONNECTION_STRING='Server=localhost,1433;User Id=sa;Password=Your_password123;Encrypt=False;TrustServerCertificate=True;Initial Catalog=master'`

## Design Constraints You Must Keep

- v1 は additive-only。
  - 実行対象は `CreateTable`, `AddColumn`, `AddConstraint`, `CreateIndex`, `AddForeignKey`, `AddDescription`, `UpdateDescription`。
  - DROP/破壊的 ALTER は実行しない。

- `desired` SQL は許容文のみ受け付ける。
  - 主に `CREATE TABLE`, `ALTER TABLE ... ADD ...`, `CREATE INDEX`, `EXEC sp_addextendedproperty`。
  - 非対応は fail-fast。

- `SchemaDiffer` の比較結果で、非加算差分は `Skipped` として返す。

- 互換性要件
  - `SqlSchemaDef.Core` / `SqlSchemaDef.SqlServer` では `netstandard2.0` 互換を維持。

## Editing Checklist

コード変更時は以下を最低限確認:

1. `dotnet build SqlSchemaDef.sln -c Release`
2. `dotnet test SqlSchemaDef.sln -c Release`
3. 変更が仕様に影響する場合:
   - `docs/dotnet-sqldef-scriptdom-visitor-spec.md`
   - `docs/dotnet-sqldef-syscatalog-queries.md`
   - 必要なら他設計 docs

## Coding Conventions

- `.editorconfig` 準拠（4 spaces, braces など）。
- Analyzer は build 時に有効（StyleCop / Roslynator / .NET analyzers）。
- 命名・正規化は既存実装に合わせる。
  - 識別子キーは `IdentifierHelper.NormalizeNameKey` / `BuildTableKey` を使う。

## Practical Notes for Agents

- 既存変更を巻き戻さない（明示依頼がある場合を除く）。
- 仕様変更を伴う実装では、テスト追加/更新を同時に行う。
- まず失敗テストを再現し、最小修正で Green に戻す。
- SQL 文字列の互換性は、単体テストだけでなく可能なら実 DB でも検証する。
