# dotnet-sqldef v1 実装タスクリスト（TDD版）

目的: `docs/dotnet-sqldef-plan.md` の v1（SQL Server / dbo固定 / 追加のみ）を、**テスト駆動（Red → Green → Refactor）**で実装するための作業チェックリスト。

前提:
- `SqlSchemaDef.Core`（公開API + Plan表現）と `SqlSchemaDef.SqlServer`（SQL Server 実装）を分離する
- 追加のみ（破壊的DDLは出さない）
- desired に許可外DDLが混ざれば即エラー

---

## 進め方（TDDルール）

- 1PR（または1コミット）= 1つの小さい振る舞い
- すべてのタスクは原則 **テストを先に追加**（Red）し、その後に実装（Green）、最後に整理（Refactor）
- 完了条件（Definition of Done）:
  - [ ] 追加したテストが意図した失敗をする（Redを確認）
  - [ ] 実装後にテストが通る（Green）
  - [ ] `dotnet test SqlSchemaDef.sln -c Release` が通る
  - [ ] 公開API変更がある場合、`docs/dotnet-sqldef-api-design.md` を更新（必要時）

---

## 0. リポジトリ/雛形（完了）

- [x] ソリューション作成: `SqlSchemaDef.sln` / `SqlSchemaDef.slnx`
- [x] プロジェクト構成作成: `src/SqlSchemaDef.Core`（`netstandard2.0`）
- [x] プロジェクト構成作成: `src/SqlSchemaDef.SqlServer`（`netstandard2.0`）
- [x] プロジェクト構成作成: `src/SqlSchemaDef.Cli`（`net8.0`）
- [x] プロジェクト構成作成: `tests/SqlSchemaDef.Tests`（xUnit, `net8.0`）
- [x] 参照関係: `Cli -> SqlServer -> Core` / `Tests -> Core`
- [x] Core 公開API雛形（Plan/Options/例外/ToScript）追加
- [x] SqlServer Planner/Applier 雛形追加（Planは現状空を返す）
- [x] `MigrationPlan.ToScript()` のユニットテスト追加

---

## 1. desired: GO 分割（BatchSplitter）

参照: `docs/dotnet-sqldef-scriptdom-visitor-spec.md`（GO分割仕様）

- [ ] テスト: `GO` の基本分割（大文字小文字、前後空白）
- [ ] テスト: `-- GO` / `/* GO */` は区切らない
- [ ] テスト: 最終バッチが空でもエラーにしない（空はスキップ）
- [ ] テスト: `GO 2` は `UnsupportedBatchSeparatorException`（行番号付き）
- [ ] 実装: `SqlSchemaDef.SqlServer` に `BatchSplitter`（`BatchIndex`/`StartLine`/`Text`）
- [ ] リファクタ: 分割ロジックと行番号補正を整理（重複排除）

---

## 2. desired: ScriptDom パース（DesiredSqlParser）

参照:
- `docs/dotnet-sqldef-scriptdom-visitor-spec.md`（parser方針/例外テンプレ）

- [ ] テスト: ScriptDom の parse error を `DesiredSqlParseException` で返す（`Diagnostics` 全件）
- [ ] テスト: `Diagnostics` に `BatchIndex`/`Line`/`Column` が入る（`StartLine` 補正込み）
- [ ] 実装: `DesiredSqlParser`（`TSql160Parser` 等を固定、`initialQuotedIdentifiers: true`）
- [ ] 実装: parse error の `Line` を `StartLine` 加算して補正

---

## 3. desired: Visitor（許可DDLのみ）

参照:
- `docs/dotnet-sqldef-scriptdom-visitor-spec.md`（受理DDL/非対応機能/例外）

### 3.1 許可外ステートメント（即エラー）
- [ ] テスト: 例）`CREATE VIEW` が `UnsupportedDesiredStatementException`
- [ ] テスト: 例）`ALTER TABLE ... ALTER COLUMN` が `UnsupportedDesiredStatementException`
- [ ] 実装: 許可外はテンプレ(A)で例外（batch/line/column を含める）

### 3.2 dbo 固定違反（即エラー）
- [ ] テスト: `CREATE TABLE foo.X (...)` は `UnsupportedSchemaException`
- [ ] テスト: FK参照先が dbo 以外は `UnsupportedSchemaException`
- [ ] 実装: schema 解決と dbo 固定チェック

### 3.3 許可DDL内の非対応機能（即エラー）
- [ ] テスト: `CREATE INDEX ... INCLUDE (...)` は `UnsupportedDesiredFeatureException`
- [ ] テスト: filtered index（`WHERE ...`）は `UnsupportedDesiredFeatureException`
- [ ] テスト: index `WITH (...)` / `ONLINE` は `UnsupportedDesiredFeatureException`
- [ ] テスト: computed column は `UnsupportedDesiredFeatureException`
- [ ] 実装: FeatureName/StatementType を埋めて例外化

### 3.4 CreateTable/AlterTableAdd/CreateIndex のモデル化
- [ ] テスト: `CREATE TABLE dbo.T (...)` が desired モデルに入る
- [ ] テスト: `ALTER TABLE dbo.T ADD Col int NULL` が desired モデルに入る
- [ ] テスト: `ALTER TABLE dbo.T ADD Col int NOT NULL` は既定で skipped（`NotNullAddNotSupported`）になる（※方針に合わせて）
- [ ] テスト: `CREATE UNIQUE INDEX ...` が desired モデルに入る
- [ ] 実装: Visitor + 最小中間モデル（v1に必要な最小限）

---

## 4. current: sys カタログ取得（CurrentSchemaReader）

参照: `docs/dotnet-sqldef-syscatalog-queries.md`

- [ ] テスト（統合 or 低レベル）: tables/columns の取得結果をモデル化できる
- [ ] テスト（ユニット）: 型文字列化（`nvarchar(max)`、`decimal(p,s)` 等）の期待値
- [ ] 実装: sys 取得SQLをコード化（dbo固定）
- [ ] 実装: 型文字列化ユーティリティ
- [ ] 実装: current 側の v1非対応要素（computed/INCLUDE/filtered/descending 等）の検出と扱い（skippedに寄せる方針で固定）

---

## 5. Diff（追加のみ）→ MigrationPlan

参照:
- `docs/dotnet-sqldef-plan.md`（v1の差分方針）
- `docs/dotnet-sqldef-test-plan.md`（順序/Skipped）

### 5.1 追加 only の operations
- [ ] テスト: current になければ `CREATE TABLE` が出る
- [ ] テスト: current になければ `ALTER TABLE ADD COLUMN` が出る（NULL可のみ）
- [ ] テスト: current になければ PK/UQ/CK が出る
- [ ] テスト: current になければ INDEX が出る
- [ ] テスト: FK は参照テーブル作成後に出る（順序）
- [ ] 実装: `SchemaDiffer`（operations 生成）

### 5.2 変更/削除は skipped
- [ ] テスト: current のみ存在（削除相当）は `SkippedReason.DropNotSupported`
- [ ] テスト: 定義差（変更相当）は `SkippedReason.AlterNotSupported`
- [ ] テスト: current 側の非対応要素は operations に出ず skipped に寄る（方針に合わせて）
- [ ] 実装: `SkippedItem` 収集（Target/Message の規約を固定）

### 5.3 決定性（順序/同順位の名前順）
- [ ] テスト: 同じ入力で `Operations`/`Skipped` の順序が常に一致
- [ ] 実装: `OperationKind` + 名前順でソート

---

## 6. ToScript（レビュー出力）

参照:
- `docs/dotnet-sqldef-test-plan.md`（ToScript検証）

- [x] テスト: 空 plan のヘッダ出力
- [x] テスト: operations + skipped の整形
- [ ] テスト: `TerminateWithSemicolon=false` の出力
- [ ] テスト: `IncludeSkipped=false` の出力
- [ ] テスト: 改行が `ScriptOptions.NewLine` に従う（`\n` 固定でスナップショット安定化）
- [ ] 実装: `ToScript()` の最終仕様確定（文言/順序）

---

## 7. Apply（SQL実行）

参照:
- `docs/dotnet-sqldef-api-design.md`（ApplyFailedException 契約）

- [ ] テスト（統合）: operations を順に実行して DB が desired に近づく
- [ ] テスト（統合）: 失敗時 `ApplyFailedException.Operation` が参照できる
- [ ] テスト（統合）: TransactionMode=SingleTransaction で一括実行（失敗時ロールバック）
- [ ] 実装: `SqlServerSchemaApplier` の挙動固め（ログ/例外/キャンセル）

---

## 8. CLI（最低限→運用向け）

- [ ] テスト（スモーク）: `--help` が usage を出す
- [ ] テスト（スモーク）: dry-run が ToScript を出す（exit code 0）
- [ ] テスト（スモーク/任意）: `--apply` で Apply する
- [ ] 実装: exit code 規約（parse/非対応/apply失敗）
- [ ] 実装: `--schema`（v1はdbo固定なので、将来用に隠し/未実装でも可）方針決定

---

## 9. 統合テスト（Docker SQL Server）

参照: `docs/dotnet-sqldef-test-plan.md`

- [ ] テスト基盤: Docker SQL Server 起動（接続文字列を環境変数で注入）
- [ ] テスト基盤: 1テスト=1DB（DB名ユニーク、並列衝突回避）
- [ ] テスト: 冪等性（空DB→Plan/Apply→再Planで `IsEmpty == true`）を 5-10 パターン
- [ ] テスト: current 余剰があっても drop を出さない（skippedに寄る）
- [ ] テスト: “既存行あり + NOT NULL列追加” が skipped になる

---

## 10. CI/品質

- [ ] CI: ユニットは常時 `dotnet test`
- [ ] CI: 統合は Docker サービス起動 + 失敗時ログ出力
- [ ] ドキュメント: 仕様変更が発生したら関連ドキュメント（plan/api/spec/test-plan）も追従

