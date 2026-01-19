# v1 sysカタログ取得SQL（dbo固定 / 追加のみ）

本書は `dotnet-sqldef-plan.md` の v1 方針に基づき、SQL Server の current schema を `sys.*` から読み取って中間モデル化するための **必要なSELECT群**を確定する。

前提:
- 対象スキーマは `dbo` 固定（`@schema = N'dbo'`）
- 取得対象はユーザー定義テーブルのみ（`sys.tables`）
- “DDLを復元”せず、メタデータを最小限のモデルへ直接マッピングする

---

## 0. 共通パラメータ

```sql
DECLARE @schema sysname = N'dbo';
```

アプリ側は `@schema` をパラメータとして渡す想定（v1は固定値）。

---

## 1. テーブル一覧（dbo）

用途:
- `TableModel` の作成（dbo固定なので Name だけでもよい）

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  s.name  AS schema_name,
  t.name  AS table_name,
  t.object_id
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
ORDER BY t.name;
```

---

## 2. 列（型/NULL/IDENTITY）

用途:
- `ColumnModel` の作成（型は v1では “文字列” として保持）
- `IDENTITY` 判定（`sys.identity_columns` の存在で判定）

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  c.column_id,
  c.name AS column_name,
  c.is_nullable,

  ty.name AS type_name,
  c.max_length,
  c.precision,
  c.scale,

  c.is_computed,

  CASE WHEN ic.object_id IS NULL THEN 0 ELSE 1 END AS is_identity,
  ic.seed_value,
  ic.increment_value
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.columns AS c
  ON c.object_id = t.object_id
JOIN sys.types AS ty
  ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.identity_columns AS ic
  ON ic.object_id = c.object_id
 AND ic.column_id = c.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
ORDER BY t.name, c.column_id;
```

メモ（型文字列化の指針）:
- v1は “変更をしない” ため、`type_name` と必要に応じて `(max_length/precision/scale)` から **表示用**の型文字列を生成して保持する
- `max_length = -1` は `MAX`
- `nchar/nvarchar` は `max_length` がバイト単位なので `/2` を考慮
- `decimal/numeric` は `(precision, scale)`
- `datetime2/time/datetimeoffset` は `scale` が小数秒精度

`c.is_computed = 1` は v1 非対応とし、desired 側に計算列が含まれる場合は即エラー推奨。current 側にある場合は “削除相当” と同様に `-- Skipped:` の対象にする（B方針）。

---

## 3. DEFAULT（列既定値）

用途:
- `ColumnModel.DefaultExpression` を raw で保持（差異は変更DDLを出さず `-- Skipped:`）

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,
  c.column_id,
  c.name AS column_name,
  dc.name AS default_name,
  dc.definition AS default_definition
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.columns AS c
  ON c.object_id = t.object_id
LEFT JOIN sys.default_constraints AS dc
  ON dc.parent_object_id = c.object_id
 AND dc.parent_column_id = c.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
  AND dc.object_id IS NOT NULL
ORDER BY t.name, c.column_id;
```

---

## 4. PK / UNIQUE（キー制約）

用途:
- `ConstraintModel(PK/UQ)` の作成
- 列順の取得

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  kc.name AS constraint_name,
  kc.type AS constraint_type,          -- 'PK' or 'UQ'
  i.name AS index_name,
  ic.key_ordinal,
  c.name AS column_name
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.key_constraints AS kc
  ON kc.parent_object_id = t.object_id
JOIN sys.indexes AS i
  ON i.object_id = t.object_id
 AND i.index_id = kc.unique_index_id
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id
 AND ic.index_id = i.index_id
 AND ic.key_ordinal > 0
JOIN sys.columns AS c
  ON c.object_id = t.object_id
 AND c.column_id = ic.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
  AND kc.type IN ('PK', 'UQ')
ORDER BY t.name, kc.name, ic.key_ordinal;
```

注:
- v1は “変更しない” ので、`is_clustered` 等の属性差は拾っても `-- Skipped:` として通知する方針でよい

---

## 5. CHECK（チェック制約）

用途:
- `ConstraintModel(CK).Definition` を raw で保持（差異は `-- Skipped:`）

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  cc.name AS constraint_name,
  cc.definition AS check_definition,
  cc.is_disabled,
  cc.is_not_trusted
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.check_constraints AS cc
  ON cc.parent_object_id = t.object_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
ORDER BY t.name, cc.name;
```

---

## 6. インデックス（PK/UQ由来以外）

用途:
- `IndexModel` の作成（UNIQUE含む）
- v1では PK/UQ は制約として扱うので、ここではそれ以外のインデックスを対象とする

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  s.name AS schema_name,
  t.name AS table_name,
  t.object_id,

  i.index_id,
  i.name AS index_name,
  i.is_unique,
  i.type_desc,
  i.is_primary_key,
  i.is_unique_constraint,

  ic.key_ordinal,
  ic.is_included_column,
  ic.is_descending_key,
  c.name AS column_name
FROM sys.tables AS t
JOIN sys.schemas AS s
  ON s.schema_id = t.schema_id
JOIN sys.indexes AS i
  ON i.object_id = t.object_id
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id
 AND ic.index_id = i.index_id
JOIN sys.columns AS c
  ON c.object_id = t.object_id
 AND c.column_id = ic.column_id
WHERE s.name = @schema
  AND t.is_ms_shipped = 0
  AND i.name IS NOT NULL
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
ORDER BY t.name, i.name, ic.is_included_column, ic.key_ordinal, c.name;
```

v1の扱い:
- `ic.is_included_column = 1`（INCLUDE）は v1非対応とし、desiredに含まれる場合は即エラー推奨
- currentに存在する INCLUDE/filtered 等は “削除相当” と同様に `-- Skipped:` で通知

---

## 7. FOREIGN KEY（外部キー）

用途:
- `ConstraintModel(FK)` の作成
- 参照元/参照先の列順を取得

```sql
DECLARE @schema sysname = N'dbo';

SELECT
  ps.name AS parent_schema_name,
  pt.name AS parent_table_name,
  pt.object_id AS parent_object_id,

  fk.name AS foreign_key_name,

  rs.name AS referenced_schema_name,
  rt.name AS referenced_table_name,
  rt.object_id AS referenced_object_id,

  fkc.constraint_column_id AS ordinal,
  pc.name AS parent_column_name,
  rc.name AS referenced_column_name,

  fk.delete_referential_action_desc,
  fk.update_referential_action_desc
FROM sys.foreign_keys AS fk
JOIN sys.tables AS pt
  ON pt.object_id = fk.parent_object_id
JOIN sys.schemas AS ps
  ON ps.schema_id = pt.schema_id
JOIN sys.tables AS rt
  ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas AS rs
  ON rs.schema_id = rt.schema_id
JOIN sys.foreign_key_columns AS fkc
  ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc
  ON pc.object_id = pt.object_id
 AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc
  ON rc.object_id = rt.object_id
 AND rc.column_id = fkc.referenced_column_id
WHERE ps.name = @schema
  AND pt.is_ms_shipped = 0
ORDER BY pt.name, fk.name, fkc.constraint_column_id;
```

v1の扱い:
- 参照先が dbo 以外の場合は v1 非対応とし、currentで検出したら `-- Skipped:`（削除相当と同等）に寄せる
- desiredに dbo 以外の参照が含まれたら即エラー推奨（スコープ外）

---

## 8. 取得結果のマッピング要点（実装メモ）

- TableKey: `dbo` + `table_name`（case-insensitive）
- ColumnKey: `dbo` + `table_name` + `column_name`（case-insensitive）
- ConstraintKey / IndexKey:
  - v1は “名前が同じなら同一” として扱う（定義差は変更扱い→ `-- Skipped:`）
- 列順:
  - PK/UQ/Index は `key_ordinal`、FK は `constraint_column_id`

---

## 9. まとめ（v1に必要なSELECT群）

v1で必須:
- 1: tables
- 2: columns（型/NULL/IDENTITY）
- 3: defaults
- 4: PK/UQ
- 5: CHECK
- 6: indexes（PK/UQ以外）
- 7: FK

これらの結果から current モデルを組み立て、desired（ScriptDom）モデルとの差分を “追加のみ” で `MigrationPlan` に落とす。

