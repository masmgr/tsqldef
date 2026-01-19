# v1 テスト計画（SQL Server専用 / dbo固定 / 追加のみ）

本書は v1 の仕様（dbo固定・追加のみ・変更/削除は `-- Skipped:`・desired は許可DDL以外エラー）に対して、品質を担保するためのテスト計画をまとめる。

ターゲット:
- ライブラリは **.NET Standard 2.0**
- テストプロジェクトは任意（例: `net8.0`）だが、参照先ライブラリの公開APIは .NET Standard 2.0 互換であることを前提にする

---

## 1. テストのゴール（v1）

- **正しさ**: 追加のみの差分DDLが正しく生成される
- **安全性**: 変更/削除/非対応DDLを混ぜた場合に確実に止まる（エラー）／実行しない（Skipped）
- **冪等性**: `Apply -> 再Plan` で `IsEmpty == true` になる
- **決定性**: 同じ入力から常に同じ `Operations` 順と `ToScript()` 出力が得られる
- **デバッグ容易性**: エラーが「何が・どこで」起きたか（batch/line/column）を示す

---

## 2. テストの層（推奨）

1) **ユニット（DB不要）**
- ScriptDom の GO分割、AST→モデル、モデル→Plan、ToScript、例外メッセージの検証

2) **統合（Docker SQL Server）**
- sysカタログ取得→モデル化、Plan/Apply の往復、冪等性の検証

3) （任意）**スモーク（CLIがある場合）**
- 実行パスをエンドツーエンドで確認（CIの最後に軽く）

---

## 3. テスト環境

### 3.1 .NET テストランナー
- xUnit（推奨） or NUnit/MSTest（どれでも可）
- Snapshot テスト: Verify.Xunit など（`ToScript()` のゴールデン）

### 3.2 SQL Server（統合テスト）
- Docker: `mcr.microsoft.com/mssql/server`（例: 2019-latest）
- 接続: `Microsoft.Data.SqlClient`
- DB名はテストごとにユニーク（並列実行時の衝突回避）
- 1テスト=1DB を推奨（確実な独立性）

---

## 4. テストカテゴリと対象

### 4.1 ScriptDom: GO分割
目的:
- `GO` 行の正しい分割、コメント内 GO を無視、`GO 123` エラー、行番号補正の正しさ

主なケース:
- `GO`（大文字/小文字、前後空白）
- `-- GO` / `/* GO */` は区切らない
- `GO 2` は `Unsupported batch separator` エラー
- 最終バッチが空でもエラーにならない（空はスキップ）

検証:
- バッチ数、各バッチ本文、`StartLine` の値

### 4.2 desired パース: 許可/不許可ステートメント
目的:
- v1で許可されるステートメントのみ受理される

許可:
- `CREATE TABLE`
- `ALTER TABLE ... ADD ...`（列/制約）
- `CREATE INDEX` / `CREATE UNIQUE INDEX`

不許可（例）:
- `DROP`, `ALTER COLUMN`, `CREATE VIEW`, `INSERT`, `EXEC`, `MERGE`, `CREATE SCHEMA`, `CREATE TRIGGER`

検証:
- 例外型とメッセージ（`Unsupported desired statement in v1...`）
- 可能なら batch/line/column が含まれる

### 4.3 desired 機能制限（許可ステートメント内の非対応）
目的:
- v1非対応機能が混入した場合に確実にエラー（もしくは仕様通り skipped）になる

主なケース（エラー推奨）:
- `CREATE INDEX ... INCLUDE (...)`
- filtered index: `WHERE ...`
- index `WITH (...)` / `ONLINE`
- computed column
- `dbo` 以外のスキーマ参照（テーブル名/参照先）
- FK の `ON UPDATE/DELETE` 指定（v1で扱わないならエラー）
- 制約名省略（v1で非対応にする場合）

検証:
- `Unsupported desired feature in v1...` / `Unsupported schema in v1...` などのテンプレに一致

### 4.4 モデル→Plan（追加のみ）
目的:
- current/desired の差分から「追加のみ」の operations と skipped が生成される

観点:
- 新規テーブル: `CREATE TABLE`
- 列追加: `ALTER TABLE ADD COLUMN`（NULL可のみ）
- 追加制約: `ALTER TABLE ADD CONSTRAINT`（PK/UQ/CK/FK）
- 追加インデックス: `CREATE INDEX`
- 変更相当（型/NULL/DEFAULT/CK定義違い等）は operations に出ない（skippedへ）
- 削除相当（currentのみ存在）は operations に出ない（skippedへ）

検証:
- `Operations` の件数と中身（Description/Kind/Target）
- `Skipped` の件数と `Reason`

### 4.5 DDL順序（決定性 + 依存関係）
目的:
- 安全な順序で operations が並ぶ（決定的）

順序要件（v1）:
1. CreateTable
2. AddColumn
3. AddConstraint(PK/UQ/CK)
4. CreateIndex
5. AddForeignKey

ケース:
- FKが参照するテーブルが同一 desired に含まれる（必ず先に作成される）
- 複数テーブル/複数FKでも順序が安定（同順位は名前順）

検証:
- `OperationKind` の並び
- `ToScript()` の順序が固定

### 4.6 ToScript（レビュー出力）
目的:
- `-- Skipped:` の整形が仕様通り
- `;` の有無などオプションが効く

ケース:
- operations 0 / skipped >0
- operations >0 / skipped 0
- 両方あり

検証:
- Snapshot（ゴールデンファイル）で差分をレビューしやすくする

---

## 5. 統合テスト（DBあり）

### 5.1 sysカタログ→モデル化
目的:
- `dotnet-sqldef-syscatalog-queries.md` の SELECT 群が期待通りに current モデルを構築できる

ケース:
- 単純な1テーブル（int/varchar/nvarchar/decimal/datetime2 等）
- IDENTITY列
- DEFAULT付き列
- PK/UQ/CK/FK
- インデックス（PK/UQ以外）

検証:
- current モデルの各要素が存在し、列順/定義が取れている

### 5.2 冪等性（最重要）
手順:
1. 空DBを作成
2. desired を Plan → Apply
3. 同じ desired を再度 Plan

期待:
- 2回目の plan が `IsEmpty == true`
- `Skipped` は（仕様上）0が望ましいが、v1非対応要素が current にある場合は存在しうる

### 5.3 追加のみの安全性
ケース:
- current に余計なテーブル/列/制約/インデックスがある
期待:
- DROP は出ない
- `SkippedReason.DropNotSupported` が出る

### 5.4 NOT NULL 追加の安全策
ケース:
- 既存行があるテーブルに `ALTER TABLE ADD <col> NOT NULL`
期待:
- operations には出ない
- `SkippedReason.NotNullAddNotSupported`

### 5.5 current 側の非対応要素（v1）
ケース（例）:
- current に computed column / INCLUDE index / filtered index / descending key など v1非対応の要素が存在
期待:
- v1は変更/削除を出さないため operations には出ない
- desired に無い場合は削除相当として `SkippedReason.DropNotSupported`
- desired に同名オブジェクトがあるが定義差となる場合は変更相当として `SkippedReason.AlterNotSupported`

---

## 6. テストデータ設計（推奨）

### 6.1 desired SQL のテンプレ
- `desired/*.sql` を用意し、各テストはそこから読み込む（可読性）
- GO/コメントの混在ケースを専用ファイルで持つ

### 6.2 current の作成
- 統合テストでは「current用SQL」を実行して状態を作る（sys取得の正しさを担保）
- current のDDLはなるべく標準的な書き方に揃える（SQL Serverが受理する範囲で）

---

## 7. 非機能テスト（v1で最低限）

- **キャンセル**: `ISchemaPlanner.PlanAsync` / `ISchemaApplier.ApplyAsync` が `CancellationToken` を尊重する（長めの desired で確認）
- **決定性**: 同じ入力を N 回実行して `ToScript()` が一致（ユニットでOK）

性能は v1 では “劣化がない” 程度の軽い確認に留める（必要ならベンチマークを別途用意）。

---

## 8. CI 実行プラン（推奨）

- `dotnet test`（ユニット）: 常時
- `dotnet test`（統合）:
  - Docker SQL Server をサービス起動
  - 環境変数で接続文字列を注入
  - 失敗時にコンテナログを出す

マトリクス（任意）:
- SQL Server: 2019-latest（まずは1つ）→ 安定後に 2022/2025 を追加
- OS: Linux（CI）を基本に、Windowsは後追いでも可

---

## 9. 受け入れ基準（v1の完了条件）

- ユニット: 主要ケース（許可/不許可、非対応機能、順序、ToScript、Skipped）が網羅されている
- 統合: “空DBから Apply→再Plan で空” を少なくとも 5-10 パターンで確認できる
- 例外メッセージ: batch index/line/column が出ること（最低でも parse error / unsupported statement）
- 追加のみ: 変更/削除に該当するDDLが operations に混ざらないことをテストで担保
