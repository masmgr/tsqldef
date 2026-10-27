# Schema Change Classification

tsqldef v1 follows an **additive-only by default** policy. Both planning and applying require explicit `AllowDrop` / `--allow-drop` permission for supported non-additive Operations. Without permission, these differences are reported as `Skipped`. Rebuild proposals have separate `--swap` permission.
Schema differences are classified into four categories:

| Category | Data Migration | Execution |
|---|---|---|
| **Additive** | Not required | Executed directly via `apply` |
| **Recreate / supported ALTER / DROP** | Depends on change | Requires `--allow-drop` for both `plan` and `apply` |
| **Rebuild (data migration)** | Required | Executed via `apply --swap` |
| **Unsupported (skipped)** | - | Manual intervention required |

---

## 1. Additive Changes (No Data Migration)

Addition of objects that do not exist in the current database, or updates to extended properties.
These are executed directly by the `plan` / `apply` commands.

| Operation | Generated SQL | Condition |
|---|---|---|
| Create new table | `CREATE TABLE` | Table does not exist in current DB |
| Add column | `ALTER TABLE ... ADD <column>` | Column does not exist on existing table |
| Add constraint (PK / UNIQUE / CHECK / DEFAULT) | `ALTER TABLE ... ADD CONSTRAINT` | Constraint with the same name does not exist |
| Create index | `CREATE [UNIQUE] [CLUSTERED|NONCLUSTERED] INDEX` | Index with the same name does not exist (and no clustered conflict) |
| Add foreign key | `ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY` | FK with the same name does not exist |
| Add description (MS_Description) | `sp_addextendedproperty` | Extended property does not exist |
| Update description (MS_Description) | `sp_updateextendedproperty` | Extended property value differs |

---

## 2. Recreate Changes (DROP + ADD, No Data Migration)

When a constraint or index definition differs between desired and current, tsqldef can generate a `DROP` followed by `ADD/CREATE` in a single operation with `--allow-drop`. Without the flag, the difference is `AlterNotSupported`. CHECK expression comparison preserves string-literal case; `'A'` and `'a'` are distinct, while identifier case is normalized.

### Constraint Recreate (`RecreateConstraint` / `RecreateForeignKey`)

| Constraint Type | Generated SQL | Example |
|---|---|---|
| PRIMARY KEY | Drop dependent FKs, replace PK, restore desired FKs | Key columns or clustering changed |
| UNIQUE | `DROP CONSTRAINT` + `ADD CONSTRAINT ... UNIQUE` | Column list changed |
| CHECK | `DROP CONSTRAINT` + `ADD CONSTRAINT ... CHECK` | Expression changed |
| DEFAULT | `DROP CONSTRAINT` + `ADD CONSTRAINT ... DEFAULT` | Default value changed |
| FOREIGN KEY | `DROP CONSTRAINT` + `ADD CONSTRAINT ... FOREIGN KEY` | ON DELETE/UPDATE action changed, reference columns changed |

When recreating a PRIMARY KEY, dependent FKs are restored from the desired definition, including ON DELETE/UPDATE actions. FKs removed from desired are not restored. Dependencies on tables excluded from the scope retain their current definitions. A covered FK is identified by schema, table, and name, and its standalone operation is suppressed to avoid duplication.

FK drops are included in the PRIMARY KEY replacement operation. Their restores are separate `RecreateForeignKey` operations executed after all key constraints and indexes have been created or replaced. This permits desired FKs to reference newly created tables/keys. These restores still require `--allow-drop`; skipping the PK replacement must not leave an additive FK restore behind.

### Index Recreate (`RecreateIndex`)

| Scenario | Generated SQL |
|---|---|
| Index definition differs (same name) | `DROP INDEX` + `CREATE INDEX` |
| Clustered index conflict (different name) | `DROP INDEX` (existing clustered) + `CREATE CLUSTERED INDEX` (desired) |

The old clustered index is consumed by recreation and must not also receive a surplus `DropIndex` operation. If the desired schema retains that index as nonclustered, it is recreated first and the new clustered index is created afterward.

Without `--allow-drop`, creation of the replacement clustered index is skipped together with the prerequisite conversion/removal of the existing clustered index. A dependent `CreateIndex` must not remain in the default additive plan.

All index properties are compared: uniqueness, clustering, key columns (incl. sort direction), INCLUDE columns, filter predicate, WITH options (excluding `ONLINE` and `SORTINTEMPDB`).

---

## 3. Changes Requiring Rebuild (Data Migration)

Unsafe column type changes, IDENTITY changes (including seed/increment), and explicit column reordering require a shadow-table rebuild. Supported type widening, nullability, and collation changes may instead use ALTER COLUMN with `--allow-drop`; otherwise they are skipped and can produce column rebuild proposals with `--emit-swap-sql`.

### Column Properties That Trigger Rebuild

`SchemaDiffer.ClassifyColumnChange()` compares the following properties:

| Property | Comparison | Example |
|---|---|---|
| Data type (`SqlType`) | Case-insensitive | `int` → `bigint`, `nvarchar(50)` → `nvarchar(100)` |
| Nullability (`IsNullable`) | Boolean | `NOT NULL` → `NULL`, `NULL` → `NOT NULL` |
| Identity | Presence plus numeric seed/increment | Adding/removing IDENTITY, `(1,1)` → `(1000,10)` |
| Default expression (`DefaultExpression`) | Case-insensitive | `(0)` → `(1)`, adding or removing DEFAULT |
| Collation (`Collation`) | Case-insensitive | `Latin1_General_CI_AS` → `Japanese_CI_AS` |

### Rebuild Process (9 Steps)

Data is migrated using a shadow-table swap approach:

| # | Step | Action |
|---|---|---|
| 1 | Create shadow table | Desired columns, including DEFAULT expressions and IDENTITY arguments |
| 2 | Drop constraints on original | `ALTER TABLE ... DROP CONSTRAINT` |
| 3 | Drop indexes on original | `DROP INDEX` |
| 4 | Copy data | `INSERT INTO shadow SELECT ... FROM original` with IDENTITY_INSERT when needed |
| 5 | Rename original table | `sp_rename 'original' → 'TableName_old'` |
| 6 | Rename shadow table | `sp_rename '__TableName_rebuild' → 'TableName'` |
| 7 | Recreate constraints | Desired constraints except DEFAULT, which already exists on the shadow table |
| 8 | Recreate indexes | `CREATE INDEX` |
| 9 | Drop old table | `DROP TABLE [schema].[TableName_old]` |

> **Note**: If other tables reference this table via foreign keys, the DROP/CREATE for those FKs is not auto-generated. A warning message is emitted; handle them manually.

### CLI Usage

```bash
# Generate a plan including rebuild proposals
tsqldef plan --connection <cs> --file desired.sql --emit-swap-sql

# Execute the plan (additive changes + shadow-table rebuild)
tsqldef apply --connection <cs> --file desired.sql --swap

# Review the plan in JSON format (proposals section contains details)
tsqldef plan --connection <cs> --file desired.sql --emit-swap-sql --format json
```

---

## 4. Unsupported Changes (Skipped)

The following DROP differences are skipped without `--allow-drop`. Supported ones become Operations only with explicit permission. Unsafe column changes still require rebuild proposals rather than ordinary Operations.
Use `--strict` mode (exit code 30) in CI to detect the presence of skipped items.

### DROP Operations

| Target | SkippedReason | Description |
|---|---|---|
| Drop table | `DropNotSupported` | Table exists in current DB but not in desired |
| Drop column | `DropNotSupported` | Column exists in current table but not in desired |
| Drop constraint | `DropNotSupported` | Constraint exists in current table but not in desired |
| Drop index | `DropNotSupported` | Index exists in current table but not in desired |
| Drop description | `DropNotSupported` | MS_Description exists in current DB but not in desired |

### Other

| Target | SkippedReason | Description |
|---|---|---|
| NOT NULL column add (skip mode) | `NotNullAddNotSupported` | When `NotNullColumnAddBehavior=Skip` is set |
| Unsupported feature in desired DDL | `UnsupportedFeatureInDesired` | Computed columns, partitioning, etc. |
| Unsupported feature in current DB | `UnsupportedFeatureInCurrent` | Computed columns, etc. (diff comparison is skipped) |

---

## 5. Decision Flow

```
Does the desired object exist in the current DB?
│
├─ No  → Additive operation (CREATE / ADD)
│
└─ Yes → Is there a difference?
    │
    ├─ No difference → No action
    │
    └─ Difference found → Object type?
        │
        ├─ Column        → Supported ALTER with --allow-drop; otherwise skipped
        │                  └─ --emit-swap-sql → Rebuild proposal for column changes
        │
        ├─ Primary Key   → RecreateConstraint with --allow-drop; otherwise skipped
        │
        ├─ Constraint    → RecreateConstraint / RecreateForeignKey
        │   (UNIQUE, CHECK, DEFAULT, FK)    (requires --allow-drop; otherwise skipped)
        │
        └─ Index         → RecreateIndex (requires --allow-drop; otherwise skipped)

Object exists in current DB but not in desired:
└─ DROP with --allow-drop; otherwise DropNotSupported
```

---

## 6. Comparison Logic Details

### Column Comparison (`ClassifyColumnChange`)

Compares data type, nullability, identity presence/parameters, default expression, and collation. Identity and unsafe type changes require rebuild. Supported ALTER changes require `--allow-drop`; default-only changes use constraint comparison and recreation with the same permission.

### Constraint Comparison (`IsConstraintDifferent`)

| Constraint Type | Compared Properties |
|---|---|
| PRIMARY KEY / UNIQUE | Column list (order and name), explicitly specified clustering |
| CHECK | Normalized expression, preserving string-literal case |
| FOREIGN KEY | Schema, reference table, column list, reference column list, ON DELETE / ON UPDATE actions |
| DEFAULT | Definition expression, target column name |

### Index Comparison (`IsIndexDifferent`)

| Property | Comparison |
|---|---|
| Uniqueness (`IsUnique`) | Boolean |
| Clustering (`IsClustered`) | Boolean |
| Key columns (`KeyColumns`) | Column name + sort direction (ASC/DESC) |
| INCLUDE columns | Column name list |
| Filter predicate (`FilterPredicate`) | String (trimmed before comparison) |
| WITH options | Key-value pairs (`ONLINE` and `SORTINTEMPDB` are excluded as execution-time options) |
