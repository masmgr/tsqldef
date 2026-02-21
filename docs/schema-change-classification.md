# Schema Change Classification

tsqldef v1 follows an **additive-only** policy.
Schema differences are classified into three categories:

| Category | Data Migration | Execution |
|---|---|---|
| **Additive** | Not required | Executed directly via `apply` |
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
| Create index | `CREATE [UNIQUE] [CLUSTERED|NONCLUSTERED] INDEX` | Index with the same name does not exist |
| Add foreign key | `ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY` | FK with the same name does not exist |
| Add description (MS_Description) | `sp_addextendedproperty` | Extended property does not exist |
| Update description (MS_Description) | `sp_updateextendedproperty` | Extended property value differs |

---

## 2. Changes Requiring Rebuild (Data Migration)

**Column definition changes on existing columns** fall into this category.
Since many column alterations cannot be performed with a simple `ALTER COLUMN` in SQL Server, tsqldef proposes a shadow-table rebuild approach.

### Column Properties That Trigger Rebuild

`SchemaDiffer.IsColumnDifferent()` detects differences along five axes:

| Property | Comparison | Example |
|---|---|---|
| Data type (`SqlType`) | Case-insensitive | `int` → `bigint`, `nvarchar(50)` → `nvarchar(100)` |
| Nullability (`IsNullable`) | Boolean | `NOT NULL` → `NULL`, `NULL` → `NOT NULL` |
| Identity (`IsIdentity`) | Boolean | Adding or removing IDENTITY |
| Default expression (`DefaultExpression`) | Case-insensitive | `(0)` → `(1)`, adding or removing DEFAULT |
| Collation (`Collation`) | Case-insensitive | `Latin1_General_CI_AS` → `Japanese_CI_AS` |

When any of the above differ, the column is skipped as `SkippedReason.AlterNotSupported`.
A rebuild proposal is generated when the `--emit-swap-sql` option is specified.

### Rebuild Process (8 Steps)

Data is migrated using a shadow-table swap approach:

| # | Step | Action |
|---|---|---|
| 1 | Create shadow table | `CREATE TABLE [schema].[__TableName_rebuild] (...)` with the desired definition |
| 2 | Copy data | `INSERT INTO shadow SELECT ... FROM original` (`IDENTITY_INSERT` is handled automatically when identity columns exist) |
| 3 | Drop constraints on original | `ALTER TABLE ... DROP CONSTRAINT` (PK, FK, UNIQUE, CHECK, DEFAULT) |
| 4 | Rename original table | `sp_rename 'original' → 'TableName_old'` |
| 5 | Rename shadow table | `sp_rename '__TableName_rebuild' → 'TableName'` |
| 6 | Recreate constraints | `ALTER TABLE ... ADD CONSTRAINT` (recreated with the desired definition) |
| 7 | Recreate indexes | `CREATE INDEX` (recreated with the desired definition) |
| 8 | Drop old table | `DROP TABLE [schema].[TableName_old]` |

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

## 3. Unsupported Changes (Skipped)

The following changes are detected but no SQL is generated.
Use `--strict` mode (exit code 30) in CI to detect the presence of skipped items.

### DROP Operations

| Target | SkippedReason | Description |
|---|---|---|
| Drop table | `DropNotSupported` | Table exists in current DB but not in desired |
| Drop column | `DropNotSupported` | Column exists in current table but not in desired |
| Drop constraint | `DropNotSupported` | Constraint exists in current table but not in desired |
| Drop index | `DropNotSupported` | Index exists in current table but not in desired |
| Drop description | `DropNotSupported` | MS_Description exists in current DB but not in desired |

### ALTER Operations (Not Eligible for Rebuild)

| Target | SkippedReason | Description |
|---|---|---|
| Constraint definition change | `AlterNotSupported` | PK columns, CHECK expression, FK definition, DEFAULT expression changed |
| Index definition change | `AlterNotSupported` | Key columns, INCLUDE columns, uniqueness, clustering, filter, options changed |
| Add clustered index | `AlterNotSupported` | Table already has a clustered index |

### Other

| Target | SkippedReason | Description |
|---|---|---|
| NOT NULL column add (skip mode) | `NotNullAddNotSupported` | When `NotNullColumnAddBehavior=Skip` is set |
| Unsupported feature in desired DDL | `UnsupportedFeatureInDesired` | Computed columns, partitioning, etc. |
| Unsupported feature in current DB | `UnsupportedFeatureInCurrent` | Computed columns, etc. (diff comparison is skipped) |

---

## 4. Decision Flow

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
        ├─ Column     → AlterNotSupported (skipped)
        │               └─ --emit-swap-sql → Rebuild proposal generated
        │
        ├─ Constraint → AlterNotSupported (skipped only)
        │
        └─ Index      → AlterNotSupported (skipped only)

Object exists in current DB but not in desired:
└─ DropNotSupported (skipped only)
```

---

## 5. Comparison Logic Details

### Column Comparison (`IsColumnDifferent`)

Compares the five axes described above (data type, nullability, identity, default expression, collation).
If any one of them differs, the column is skipped as `AlterNotSupported`.

### Constraint Comparison (`IsConstraintDifferent`)

| Constraint Type | Compared Properties |
|---|---|
| PRIMARY KEY / UNIQUE | Column list (order and name) |
| CHECK | Definition expression |
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
