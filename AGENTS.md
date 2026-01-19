# Agent Guide (tsqldef / dotnet-sqldef)

このリポジトリは .NET の SQL Server 向けスキーマ差分/適用ツールです。`desired.sql`（理想状態）から “additive-only v1” のマイグレーション計画を生成し、必要に応じて適用します。

## 構成

- `src/SqlSchemaDef.Core/`：DB 非依存の計画モデル（`MigrationPlan` 等）
- `src/SqlSchemaDef.SqlServer/`：SQL Server 実装（`DesiredSqlParser` / `CurrentSchemaReader` / `SqlServerSchemaPlanner` / `SqlServerSchemaApplier`）
- `src/SqlSchemaDef.Cli/`：最小 CLI（dry-run 出力と `--apply`）
- `tests/SqlSchemaDef.Tests/`：xUnit（単体 + SQL Server 統合）
- `docs/`：設計ドキュメント（入口は `docs/README.md`）

## よく使うコマンド

- Restore/Build/Test：`dotnet restore SqlSchemaDef.sln` / `dotnet build SqlSchemaDef.sln -c Release` / `dotnet test SqlSchemaDef.sln -c Release`
- CLI 実行（例）：`dotnet run --project src/SqlSchemaDef.Cli -- --help`

## 統合テスト（SQL Server）

統合テストは環境変数 `SQLSCHEMADEF_TEST_CONNECTION_STRING` が未設定の場合、早期 return してスキップします。SQL Server を立てて実行したい場合は接続文字列を設定してください（`Initial Catalog=master` 推奨）。

- 例（Docker）：`docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Your_password123 -p 1433:1433 --name tsqldef-sql --rm mcr.microsoft.com/mssql/server:2022-latest`
- 例（PowerShell）：`$env:SQLSCHEMADEF_TEST_CONNECTION_STRING='Server=localhost,1433;User Id=sa;Password=Your_password123;Encrypt=False;TrustServerCertificate=True;Initial Catalog=master'`

## 実装時の注意

- コードスタイルは `.editorconfig` に従う（4 spaces / braces など）。
- `netstandard2.0`（`SqlSchemaDef.Core` / `SqlSchemaDef.SqlServer`）に入れるコードは、`net8.0` 専用 API に依存しない。
- v1 は additive-only の前提：既存オブジェクトの Drop/破壊的変更を追加しない（必要なら “skipped” として理由を返す方向）。
- 仕様/許容 SQL の更新は `docs/`（特に `dotnet-sqldef-scriptdom-visitor-spec.md`）も合わせて更新する。

