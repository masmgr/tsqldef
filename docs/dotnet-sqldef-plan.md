# SQL Server専用 sqldef 相当（C#/.NET + ScriptDom）v1 計画

目的: SQL Serverのスキーマを **DDL（desired）** に合わせて、現在DB（current）との差分を **冪等** に適用できる仕組みを .NET プログラムへ組み込みやすい形で提供する。

本ドキュメントは v1 の設計・実装計画をまとめたもの。v1 は「安全第一」で、**新規作成 + 追加のみ** を行う。

ターゲット:
- ライブラリは **.NET Standard 2.0**
  - その範囲で利用可能な BCL 型と API のみを前提にする
  - C# 言語バージョンは任意だが、公開APIのサンプルは .NET Standard 2.0 前提の型（`record` / `required` などに依存しない形）で記述する

---

## 1. v1 スコープ（確定）

### 対象DB
- SQL Server 専用
- スキーマは **`dbo` 固定**
  - desired/current ともにスキーマ未指定は `dbo` として扱う

### 対象オブジェクト/DDL（作成・追加のみ）
- `CREATE TABLE`（dbo）
  - 列: 型、NULL/NOT NULL、IDENTITY、DEFAULT（保持のみ）
  - テーブル制約: PK / UNIQUE / CHECK / FK（追加のみ）
- `ALTER TABLE ... ADD`（dbo）
  - 列追加（推奨: v1では安全のため NULL 可のみ。NOT NULL 追加は `-- Skipped:` 扱い）
  - 制約追加（PK/UQ/CHECK/FK）
- `CREATE INDEX`（dbo、UNIQUE含む）

### v1で「絶対にしない」こと（確定）
- 変更: `ALTER COLUMN`、型変更、NULL変更、DEFAULT変更、制約定義変更、インデックス再定義など
- 削除: `DROP TABLE/COLUMN/CONSTRAINT/INDEX`、REVOKE など
- リネーム（`@renamed from=` 相当）
- 対象外構文の黙殺

### desired SQL の許容ステートメント（確定）
- desired 側に含めてよいのは **`CREATE TABLE` / `ALTER TABLE ... ADD ...` / `CREATE INDEX` のみ**
- それ以外（例: `INSERT`, `DROP`, `ALTER COLUMN`, `EXEC`, `CREATE VIEW` 等）は **即エラー**

---

## 2. 出力方針（B: Skipped通知）

- current にあるが desired にないオブジェクト（削除相当）や、差分が「変更」方向に見えるものは、
  - **DDLは生成しない**
  - 出力に `-- Skipped: <理由> <対象>` を必ず含める

例:
- `-- Skipped: drop is not supported in v1 dbo.Users.OldIndex`
- `-- Skipped: alter is not supported in v1 dbo.Users.Email DEFAULT differs`

`MigrationPlan` は `Operations`（実行DDL）と `Skipped`（通知）を分けて保持する。

---

## 3. アーキテクチャ概要

### 入力
- desired: SQL文字列（または将来的に複数ファイル）
- current: 接続先 SQL Server（dbo）

### 主要コンポーネント
- `DesiredSchemaLoader`（ScriptDom）
  - GOバッチ分割 → 各バッチを ScriptDom パース → Visitor でモデル化
- `CurrentSchemaReader`（sysカタログ）
  - `sys.*` から dbo のメタデータを取得してモデル化
- `SchemaDiffer`
  - current/desired モデルを比較し、追加のみの `MigrationPlan` を作成
- `SqlServerSchemaApplier`
  - `MigrationPlan` の operations をトランザクションで順次実行
- `MigrationPlan.ToScript()`
  - 実行予定SQLと `-- Skipped:` をレビュー用に整形

---

## 4. 中間モデル（最小）

目的: desired と current を同じ形に揃え、比較可能にする。v1 は「変更を出さない」ため、比較に必要な最小情報に絞る。

- `DatabaseModel`
  - `Tables: Dictionary<TableKey, TableModel>`（キーは dbo + case-insensitive 名）
- `TableModel`
  - `Schema = "dbo"`
  - `Name`
  - `Columns: Dictionary<ColumnKey, ColumnModel>`
  - `Constraints: Dictionary<ConstraintKey, ConstraintModel>`（PK/UQ/CK/FK）
  - `Indexes: Dictionary<IndexKey, IndexModel>`
- `ColumnModel`
  - `Name`
  - `SqlType`（文字列）
  - `IsNullable`
  - `IsIdentity`
  - `DefaultExpression`（文字列、比較は raw。差異は Skipped）
- `ConstraintModel`
  - `Kind: PK | UQ | CK | FK`
  - `Name`
  - 種別ごとの最小情報
    - PK/UQ: 列順
    - CK: `Definition`（raw、差異は Skipped）
    - FK: 参照先テーブル/列順
- `IndexModel`
  - `Name`
  - `IsUnique`
  - `KeyColumns`（列順）

正規化（v1）:
- 参照・比較のキーは case-insensitive（dbo固定）
- 式（DEFAULT/CHECK）は raw のまま保持し、差異は変更DDLを出さず `-- Skipped:` にする

---

## 5. ScriptDom パース設計（desired）

### Parser
- `TSqlParser` は固定のバージョン（例: `TSql160Parser`）。サポート対象SQL Serverをドキュメント化する。

### GO（バッチ）処理
- ScriptDomは `GO` を扱わないため、事前にバッチ分割する
- v1は「行頭の `GO`（前後空白OK）」のみ区切りとして扱う（コメント等は除外）
- Apply時は `GO` を使わず、コマンド列として実行する

### Visitor（許容ステートメントのみ）
- `CreateTableStatement`
- `AlterTableAddTableElementStatement`（ADD COLUMN / ADD CONSTRAINT）
- `CreateIndexStatement`
- それ以外が出たら **エラー**

### 追加のみを担保するルール
- `ALTER TABLE` は `ADD` のみ許容（`ALTER COLUMN` 等は即エラー）
- v1で解釈不能なオプション（filtered index、INCLUDE、computed、特殊制約等）は即エラー（誤差分を避ける）

---

## 6. sysカタログ取得設計（current）

dbo のみ取得し、中間モデルへマッピングする。DDL復元はしない。

取得対象（最小）:
- tables/schemas: `sys.tables`, `sys.schemas`
- columns/types: `sys.columns`, `sys.types`, `sys.identity_columns`
- defaults: `sys.default_constraints`
- PK/UQ: `sys.key_constraints`, `sys.indexes`, `sys.index_columns`
- CHECK: `sys.check_constraints`
- FK: `sys.foreign_keys`, `sys.foreign_key_columns`
- indexes: `sys.indexes`, `sys.index_columns`

---

## 7. 差分生成（追加のみ）と順序

`SchemaDiffer.Diff(current, desired) -> MigrationPlan`

### 生成する operations（追加のみ）
- schema固定のため `CREATE SCHEMA` は v1では基本不要（desiredに出たら非対応でエラーにしてもよい）
- `CREATE TABLE dbo.X`（currentに無いテーブル）
- `ALTER TABLE dbo.X ADD <column>`（currentに無い列）
- `ALTER TABLE dbo.X ADD CONSTRAINT ...`（currentに無いPK/UQ/CK/FK）
- `CREATE [UNIQUE] INDEX ... ON dbo.X(...)`（currentに無いインデックス）

### 生成しない（Skippedのみ）
- currentにあるが desired に無い（削除相当）
- desired/current の定義差（変更相当）
  - DEFAULT式差、CHECK式差、列定義差、制約差、インデックス差など

### 適用順序（安全）
1. `CREATE TABLE`
2. `ALTER TABLE ADD COLUMN`
3. `ALTER TABLE ADD CONSTRAINT`（PK/UQ/CK）
4. `CREATE INDEX`
5. `ALTER TABLE ADD CONSTRAINT`（FK）

---

## 8. DDL生成と実行（dry-run/apply）

### Operation表現
- `SqlOperation { Description, Sql }`
- `SkippedItem { Reason, Target, Details? }`
- `MigrationPlan { Operations, Skipped, IsEmpty }`

### ToScript（レビュー用途）
- `-- Skipped:` をまとめて表示（件数も表示できると便利）
- operations のSQLを `;` 付きで列挙

### Apply（実行）
- `Microsoft.Data.SqlClient` で順次 `ExecuteNonQuery`
- 基本は単一トランザクション
- 失敗時は「どの Operation で失敗したか」を含めて例外化（Description/SQL）

### ログ
- .NET `ILogger` を注入できる設計（Plan作成時/Apply時のトレース）

---

## 9. テスト戦略

### ユニット（DB不要）
- desired SQL → モデル化（ScriptDom変換）
- currentモデル + desiredモデル → plan（差分）
- `ToScript()` のスナップショット（順序、Skippedの文言）

主要ケース:
- 新規テーブル作成
- 列追加（NULL）
- PK/UQ/CK/FK 追加（FKは順序が正しいこと）
- currentに余計なオブジェクトがある → Skipped（削除相当）
- DEFAULT/CHECK式の差異 → Skipped（変更相当）
- desired に非対応ステートメントが混在 → エラー

### 統合（Docker SQL Server）
- `Apply → 再度 Plan` が空になる（冪等性）
- “既存行あり + NOT NULL列追加”が Skipped になる（安全ポリシーの確認）

---

## 10. パッケージング（組み込み最優先）

NuGet想定:
- `*.Core`
  - 中間モデル、Diff、MigrationPlan、ToScript、Skipped表現
- `*.SqlServer`
  - ScriptDom loader（desired）
  - sys reader（current）
  - applier（apply/dry-run）
- （任意）`*.Cli`
  - デバッグ用CLI（導入・検証が速くなる）

公開API（最小案）:
- `Task<MigrationPlan> ISchemaPlanner.PlanAsync(DbConnection, string desiredSql, PlannerOptions, CancellationToken)`
- `Task ISchemaApplier.ApplyAsync(DbConnection, MigrationPlan, ApplyOptions, CancellationToken)`
- `string MigrationPlan.ToScript(ScriptOptions options = null)`

---

## 11. ロードマップ（v1以降の候補）

- v1.1: `@renamed from=` 相当（リネーム支援）
- v1.1: `NOT NULL + DEFAULT` の安全な列追加をオプションで許可
- v1.2: 変更対応（ALTER COLUMN、DEFAULT/CK変更、インデックス再定義）
- v2: dbo固定をやめ `TargetSchemas` 対応、VIEW/TRIGGER等の拡張
