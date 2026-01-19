# v1 sys catalog queries (fixed `dbo` / additive-only)

Based on the v1 principles in `dotnet-sqldef-plan.md`, this document locks down the **required SELECT queries** to read the SQL Server `current` schema from `sys.*` and convert it into the intermediate model.

Assumptions:
- Target schema is fixed to `dbo` (`@schema = N'dbo'`)
- Read user tables only (`sys.tables`)
- Do not “reconstruct DDL”; map metadata directly into the minimal model

---

## 0. Common parameter

```sql
DECLARE @schema sysname = N'dbo';
```

The app is expected to pass `@schema` as a parameter (in v1 it is always `dbo`).

---

## 1. Table list (`dbo`)

Use:
- Create `TableModel` entries (since schema is fixed, table name alone is enough)

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

## 2. Columns (type / NULL / IDENTITY)

Use:
- Build `ColumnModel` entries (type is kept as a string in v1)
- Detect IDENTITY (by presence in `sys.identity_columns`)

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

Notes (type stringification guidance):
- Since v1 does not emit alter DDL, generate a **display** type string from `type_name` and (as needed) `(max_length/precision/scale)`
- `max_length = -1` means `MAX`
- `nchar/nvarchar` use byte lengths, so divide `max_length` by 2
- `decimal/numeric` use `(precision, scale)`
- `datetime2/time/datetimeoffset` use `scale` for fractional seconds precision

`c.is_computed = 1` is unsupported in v1. If it appears in `desired`, fail fast. If it exists only in `current`, treat it like a drop-equivalent and include it as `-- Skipped:` (policy B).

---

## 3. DEFAULT (column default constraints)

Use:
- Store `ColumnModel.DefaultExpression` as raw (`definition`); differences become `-- Skipped:` (no alter DDL)

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

## 4. PK / UNIQUE (key constraints)

Use:
- Build `ConstraintModel` for PK/UQ
- Capture key column ordering

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

Note:
- Since v1 does not alter, differences in attributes like `is_clustered` can be detected but should be reported as `-- Skipped:`

---

## 5. CHECK constraints

Use:
- Store `ConstraintModel(CK).Definition` as raw; differences become `-- Skipped:`

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

## 6. Indexes (excluding PK/UQ-backed ones)

Use:
- Build `IndexModel` entries (including UNIQUE)
- In v1, PK/UQ are treated as constraints, so this query targets other indexes

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

v1 handling:
- `ic.is_included_column = 1` (INCLUDE) is unsupported in v1; if present in `desired`, fail fast
- If `current` contains INCLUDE/filtered/etc., treat it like a drop-equivalent and report via `-- Skipped:`

---

## 7. FOREIGN KEY

Use:
- Build `ConstraintModel(FK)`
- Capture parent/referenced column ordering

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

v1 handling:
- If the referenced schema is not `dbo`, treat it as unsupported. If detected in `current`, report as `-- Skipped:` (drop-equivalent). If present in `desired`, fail fast (out of scope).

---

## 8. Mapping notes (implementation)

- TableKey: `dbo` + `table_name` (case-insensitive)
- ColumnKey: `dbo` + `table_name` + `column_name` (case-insensitive)
- ConstraintKey / IndexKey:
  - In v1, “same name means same object”. Definition differences are alter-equivalent → `-- Skipped:`
- Column ordering:
  - PK/UQ/Index: `key_ordinal`
  - FK: `constraint_column_id`

---

## 9. Summary (queries required for v1)

Required in v1:
- 1: tables
- 2: columns (type/NULL/IDENTITY)
- 3: defaults
- 4: PK/UQ
- 5: CHECK
- 6: indexes (excluding PK/UQ)
- 7: FK

Build the `current` model from these results, then compute the additive-only plan (`MigrationPlan`) against the `desired` model (ScriptDom).

