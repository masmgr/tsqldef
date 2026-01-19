# v1 ScriptDom Visitor 対象ステートメントと変換仕様（dbo固定 / 追加のみ）

本書は `dotnet-sqldef-plan.md` の v1 方針（dbo固定、追加のみ、変更/削除は `-- Skipped:`、desired は許可DDL以外エラー）に基づき、**ScriptDom Visitor が受理するステートメント一覧**と、**中間モデルへの変換仕様**、および **例外メッセージ規約**を確定する。

対象:
- desired SQL（入力DDL）のみ（current は `sys.*` 取得）
- ScriptDom: `Microsoft.SqlServer.TransactSql.ScriptDom`

---

## 1. 前処理: GO バッチ分割（必須）

### 1.1 目的
ScriptDom は `GO` を文法要素として扱わないため、`GO` で区切られた入力を事前に分割してからパースする。

### 1.2 v1 の分割ルール（確定）
- 区切りは **行頭の `GO`** のみ（前後空白は許可）
- 大文字小文字は無視（`go`, `Go` も区切り）
- `--` 行コメントや `/* */` ブロックコメントの **内部にある `GO` は区切り扱いしない**
- `GO 123`（繰り返し回数）は v1 では **非対応（エラー）**

### 1.3 分割時の行番号管理
後述の診断/例外で “元のSQLの行/列” を出せるように、各バッチに以下の情報を保持する:
- `BatchIndex`（0-based）
- `StartLine`（元SQLの 1-based 開始行）
- `Text`（バッチ本文）

`TSqlParser.Parse(...)` が返す `ParseError.Line` はバッチ内行番号なので、表示時は `StartLine` を加算して補正する。

---

## 2. Parser バージョン方針

v1 は parser を固定してサポートバージョンを明記する（例: SQL Server 2019+ 相当）。

推奨:
- `TSql160Parser`（SQL Server 2022 相当）または `TSql170Parser`（SQL Server 2025 相当）
- `initialQuotedIdentifiers: true`

---

## 3. Visitor の責務（v1）

### 3.1 許容ステートメント（トップレベル）
v1で受理するのは以下のみ:
- `CreateTableStatement`
- `AlterTableAddTableElementStatement`
- `CreateIndexStatement`

それ以外が出現したら **即エラー**（誤って破壊的/非対応DDLを混ぜた場合に確実に止める）。

### 3.2 “追加のみ” の担保
allowed の中でも次は v1 では禁止:
- `ALTER TABLE` で `ADD` 以外（`AlterTableAlterColumnStatement` 等）は即エラー（そもそもトップレベル許容外）
- `CREATE INDEX` の `INCLUDE` / `WHERE`（filtered）/ `WITH(...)` / `ONLINE` 等は v1 非対応（エラー）
- 計算列、列セット、圧縮、パーティション、特殊インデックスなど v1 非対応の属性はエラー

---

## 4. 中間モデルへの変換仕様（確定）

中間モデルの概念は `dotnet-sqldef-plan.md` の「中間モデル（最小）」に一致させる。

### 4.1 共通: 識別子と dbo 固定
- スキーマ名:
  - テーブル名にスキーマが指定されていなければ `dbo` として扱う
  - スキーマが指定されていて `dbo` 以外なら v1 非対応 → エラー
- 比較キーは case-insensitive だが、**出力の表記は desired 側の表記を優先**して保持する

#### 実装方針（推奨）
- `NormalizeSchema(Identifier)`:
  - null/empty → `"dbo"`
  - `"dbo"`（大小無視）→ `"dbo"`
  - それ以外 → エラー
- `NormalizeNameKey(string)`:
  - `ToUpperInvariant()` などで正規化して辞書キー化

### 4.2 `CreateTableStatement` → `TableModel`

受理:
- `CREATE TABLE dbo.X (...)`

禁止（エラー）:
- `CREATE TABLE` の `AS SELECT`、一時テーブル、外部テーブル、システムテーブル関連
- `WITH (...)` のストレージ/圧縮/パーティション/FILEGROUP など v1 非対応オプション
- `CREATE TABLE` の中に `INDEX` 定義がある場合（ScriptDomの表現次第だが v1では扱わない）

#### 4.2.1 列（ColumnDefinition）
マッピング:
- `ColumnModel.Name`
- `ColumnModel.IsNullable`
- `ColumnModel.IsIdentity`（IDENTITY指定の有無）
- `ColumnModel.SqlType`（以下のルールで文字列化）
- `ColumnModel.DefaultExpression`（DEFAULTがあれば raw 文字列）

型文字列化（v1推奨）:
- ScriptDom の `DataTypeReference` を元に、最小限の標準表記に整形する
  - 例: `nvarchar(255)` / `nvarchar(max)` / `decimal(18,2)` / `datetime2(7)`
- 文字列化した結果は比較では “同一性の参考” に使うが、差異があっても v1 は変更しない（`-- Skipped:`）

計算列（computed）は v1 非対応:
- `ColumnDefinition.ComputedColumnExpression != null` ならエラー

NOT NULL 列:
- `CREATE TABLE` で `NOT NULL` は許可（新規作成なので安全）
- `ALTER TABLE ... ADD` の `NOT NULL` は v1安全策により既定は skipped（後述）

#### 4.2.2 テーブル制約（TableDefinition/ConstraintDefinition）
v1で扱うのは以下:
- PRIMARY KEY
- UNIQUE
- CHECK
- FOREIGN KEY

制約名:
- 名称が省略されている場合、v1では **非対応（エラー）** を推奨
  - 理由: current 側の実名と一致判定できず、冪等性と skipped の明確化が難しくなるため
  - 将来: 自動命名規則を実装して追従する余地はある

PRIMARY KEY / UNIQUE:
- `ConstraintModel.Kind = PK/UQ`
- `ConstraintModel.Name = <constraint name>`
- 対象列名（順序）を保持
- cluster 指定等の属性は v1 では非対応 → エラー（または無視して良いが v1は安全に倒してエラー推奨）

CHECK:
- `ConstraintModel.Kind = CK`
- `ConstraintModel.Name`
- `ConstraintModel.Definition = <expression raw string>`
  - raw string は ScriptDom の ScriptGenerator で生成するか、fragment substring を保持する

FOREIGN KEY:
- `ConstraintModel.Kind = FK`
- `ConstraintModel.Name`
- 親テーブル（dbo固定）
- 親列（順序）
- 参照先テーブル/列（dbo固定）
- `ON DELETE/UPDATE` は v1では扱わない（指定があればエラー推奨）

### 4.3 `AlterTableAddTableElementStatement` → 追加操作

受理:
- `ALTER TABLE dbo.X ADD <column>`
- `ALTER TABLE dbo.X ADD CONSTRAINT ...`（PK/UQ/CK/FK）

禁止（エラー）:
- `ALTER TABLE` の `ALTER COLUMN`/`DROP`/`WITH CHECK` 等（そもそもトップレベル許容外）

列追加:
- テーブル名（dbo固定）を解決
- ColumnDefinition を `ColumnModel` に変換
- v1安全策:
  - `NOT NULL` の列追加は既定で **エラーではなく plan.Skipped**（理由: 既存行で失敗しやすい）
  - `DEFAULT` があっても v1では “安全なNOT NULL追加” を保証しないため、既定は skipped
  - skipped の `Reason = NotNullAddNotSupported`

制約追加:
- CreateTable と同じ制約変換ルール
- `ADD CONSTRAINT` で名称省略は v1では非対応（エラー推奨）

### 4.4 `CreateIndexStatement` → `IndexModel`

受理:
- `CREATE INDEX IX ... ON dbo.T(col1, col2)`
- `CREATE UNIQUE INDEX ...`

禁止（エラー）:
- `INCLUDE (...)`
- `WHERE ...`（filtered index）
- `WITH (...)`（fillfactor/online 等）
- `ON <filegroup/partition scheme>` 等
- 下降順/ASC/DESC の指定（存在する場合は v1では非対応としてエラー推奨。将来拡張）

マッピング:
- `IndexModel.Name`
- `IndexModel.IsUnique`
- `IndexModel.KeyColumns`（列順）

---

## 5. 例外仕様（メッセージ規約を確定）

### 5.1 例外型（推奨）
- `DesiredSqlParseException`（ScriptDom parse errors）
- `UnsupportedDesiredStatementException`（許可外ステートメント）
- `UnsupportedDesiredFeatureException`（許可ステートメント内の非対応機能）

### 5.2 メッセージの必須要素
いずれも以下を含める:
- エラー種別（固定文言）
- 対象バッチ番号（0-based）
- 元SQL上の行・列（1-based、可能なら）
- 対象ステートメント種別（可能なら）
- “何が非対応か” と “v1は追加のみである” のヒント

### 5.3 メッセージテンプレート（確定）

#### (A) 許可外ステートメント
```
Unsupported desired statement in v1 (additive-only).
Only CREATE TABLE / ALTER TABLE ... ADD ... / CREATE INDEX are supported.
Found: {StatementType} at batch {BatchIndex}, line {Line}, column {Column}.
```

#### (B) 非対応機能（例: CREATE INDEX INCLUDE）
```
Unsupported desired feature in v1 (additive-only).
Feature: {FeatureName}. Statement: {StatementType}.
Location: batch {BatchIndex}, line {Line}, column {Column}.
```

#### (C) dbo固定違反（例: schemaがdbo以外）
```
Unsupported schema in v1.
Only schema 'dbo' is supported. Found: '{SchemaName}'.
Location: batch {BatchIndex}, line {Line}, column {Column}.
```

#### (D) GO 反復回数
```
Unsupported batch separator in v1.
"GO {N}" is not supported; use plain "GO".
Location: line {Line}.
```

### 5.4 ScriptDom parse error の収集
`TSqlParser.Parse` の `IList<ParseError>` を全件収集し、`DesiredSqlParseException.Diagnostics` に詰める。

推奨表記:
```
Failed to parse desired SQL.
Batch {BatchIndex}, line {Line}, column {Column}: {Message}
```

---

## 6. Visitor 実装ガイド（最小構成）

### 6.1 推奨クラス分割
- `BatchSplitter`（GO分割 + StartLine 記録）
- `DesiredSqlParser`
  - `ParseBatches(IEnumerable<SqlBatch>) -> IReadOnlyList<TSqlFragment>`
  - parse errors の補正（StartLine加算）
- `DesiredModelBuilderVisitor : TSqlFragmentVisitor`
  - `Visit(CreateTableStatement)`
  - `Visit(AlterTableAddTableElementStatement)`
  - `Visit(CreateIndexStatement)`
  - それ以外は `ExplicitVisit(TSqlStatement)` で例外

### 6.2 “それ以外は例外” の実装方針
ScriptDom の Statement を列挙し、許可外に遭遇したら即例外。

推奨: `ExplicitVisit(TSqlStatement node)` を override し、許可した型は個別 override で処理し、それ以外はテンプレート(A)で例外。

---

## 7. v1での妥協点（明示）

- 制約名省略を許さない（推奨）
  - v1の冪等性を簡単に保証するための制約
  - 将来: SQL Server の自動命名規則に追従する実装で緩和可能
- DEFAULT/CHECK式は raw 文字列として保持
  - v1では差異があっても変更しないため、正規化しない
- CREATE INDEX の高度機能（INCLUDE/filtered/with/online 等）は v1非対応

