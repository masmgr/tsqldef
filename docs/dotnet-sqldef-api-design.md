# v1 Public API Detailed Design (SQL Server only / additive-only)

This document defines concrete public API types based on the v1 principles in `dotnet-sqldef-plan.md` (fixed `dbo`, additive-only, alter/drop become `-- Skipped:`, and `desired` fails fast if it contains unsupported DDL).

Targets:
- The library targets **.NET Standard 2.0**
  - Public API samples should not rely on language features like `record` / `required` / `init`
  - Since v1 is SQL Server-only, the implementation uses **`Microsoft.Data.SqlClient.SqlConnection`**
  - However, prefer keeping `SqlSchemaDef.Core` public APIs as close as possible to **`System.Data.Common.DbConnection`** so Core does not depend on a specific provider

---

## 1. Namespace and Assembly Layout (Draft)

- `SqlSchemaDef.Core`
  - `SqlSchemaDef.Core.Planning` (plan/operations/options/diagnostics)
  - `SqlSchemaDef.Core.Model` (internal model; keep public surface minimal)
- `SqlSchemaDef.SqlServer`
  - `SqlSchemaDef.SqlServer.Planning` (SQL Server planner/applier implementation)

The types below are intended to live primarily under `SqlSchemaDef.Core.Planning`.

---

## 2. Typical Usage (Embedding Side)

```csharp
using Microsoft.Data.SqlClient;
using SqlSchemaDef.SqlServer.Planning;

await using var conn = new SqlConnection(connectionString);
await conn.OpenAsync(ct);

var planner = new SqlServerSchemaPlanner();
var applier = new SqlServerSchemaApplier();

var plan = await planner.PlanAsync(conn, desiredSql, new PlannerOptions(), ct);

// For review
var script = plan.ToScript();

// Apply (do not call in dry-run)
if (!plan.IsEmpty)
{
    await applier.ApplyAsync(conn, plan, new ApplyOptions(), ct);
}
```

---

## 3. Key Interfaces

### 3.1 `ISchemaPlanner`

The planner creates a `MigrationPlan` from `current` (DB) + `desired` (SQL).

```csharp
namespace SqlSchemaDef.Core.Planning;

public interface ISchemaPlanner
{
    Task<MigrationPlan> PlanAsync(
        DbConnection connection,
        string desiredSql,
        PlannerOptions options = null,
        CancellationToken cancellationToken = default);
}
```

The SQL Server implementation is `SqlSchemaDef.SqlServer.Planning.SqlServerSchemaPlanner : ISchemaPlanner`.

Notes:
- Since v1 is SQL Server-only, `SqlServerSchemaPlanner` may require `SqlConnection` and throw if given a different `DbConnection`
- Prefer having the caller open the connection. If unopened connections are supported, document the behavior explicitly

### 3.2 `ISchemaApplier`

The applier executes `MigrationPlan.Operations` in order (it never executes `-- Skipped:`).

```csharp
namespace SqlSchemaDef.Core.Planning;

public interface ISchemaApplier
{
    Task ApplyAsync(
        DbConnection connection,
        MigrationPlan plan,
        ApplyOptions options = null,
        CancellationToken cancellationToken = default);
}
```

The SQL Server implementation is `SqlSchemaDef.SqlServer.Planning.SqlServerSchemaApplier : ISchemaApplier`.

---

## 4. `PlannerOptions` (v1: fixed `dbo` / additive-only)

The v1 principle is “safety first”. Options should default to preventing destructive output.

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class PlannerOptions
{
    // v1: fixed to dbo. You may keep this for future expansion, but treat it as immutable in v1.
    public string Schema { get; set; } = "dbo";

    // v1: error if desired SQL contains unsupported statements
    public UnsupportedDesiredStatementBehavior UnsupportedDesiredStatementBehavior { get; set; }
        = UnsupportedDesiredStatementBehavior.Error;

    // v1: additive-only. Alter/drop diffs are collected as skipped notifications.
    public PlanMode Mode { get; set; } = PlanMode.AdditiveOnly;

    // v1 safety: adding NOT NULL columns to tables that might have existing rows becomes skipped
    // (safe NOT NULL additions with DEFAULT could be added in v1.1+)
    public NotNullColumnAddBehavior NotNullColumnAddBehavior { get; set; }
        = NotNullColumnAddBehavior.Skip;

    // v1: extra objects/diffs in current are collected as skipped (not errors)
    public SurplusCurrentObjectBehavior SurplusCurrentObjectBehavior { get; set; }
        = SurplusCurrentObjectBehavior.CollectAsSkipped;
}

public enum PlanMode
{
    AdditiveOnly = 0,
}

public enum UnsupportedDesiredStatementBehavior
{
    Error = 0,
}

public enum NotNullColumnAddBehavior
{
    Skip = 0,
    // Error,  // future: fail fast if desired includes NOT NULL additions
}

public enum SurplusCurrentObjectBehavior
{
    CollectAsSkipped = 0,
    // SilentIgnore, // future: emit nothing
    // Error,        // future: strict CI mode
}
```

Design notes:
- v1 keeps `PlanMode` with a single value to make “what we never do” explicit (and to ease future expansion)
- v1 defaults are chosen to be non-destructive

---

## 5. Apply Options

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class ApplyOptions
{
    public TransactionMode TransactionMode { get; set; } = TransactionMode.SingleTransaction;

    // future: logging hooks, execution timeout, etc.
}

public enum TransactionMode
{
    SingleTransaction = 0,
    // PerOperation, // future
}
```

---

## 6. `SqlOperation` (execution unit)

In v1, use “one statement = one operation” as a rule of thumb (easier to trace failures).

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class SqlOperation
{
    public OperationKind Kind { get; set; }
    public SqlObjectRef Target { get; set; }

    public string Description { get; set; } // e.g. "Create table dbo.Users"
    public string Sql { get; set; }         // e.g. "CREATE TABLE dbo.Users (...)"

    // References for logging/errors
    public int? BatchIndex { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

public enum OperationKind
{
    CreateTable = 1,
    AddColumn = 2,
    AddConstraint = 3,
    CreateIndex = 4,
    AddForeignKey = 5,
}
```

Notes:
- `BatchIndex/Line/Column` are primarily useful for connecting operations back to `desired` SQL
- The concrete enum values are not important, but keep them stable once published

---

## 7. `SkippedItem` (notification)

Skipped is a mechanism to preserve diffs that require user judgment later.

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class SkippedItem
{
    public SkippedReason Reason { get; set; }
    public SqlObjectRef Target { get; set; }

    public string Message { get; set; } // e.g. "alter is not supported in v1: column dbo.Users.Email differs"
    public string Details { get; set; } // optional: snippets of current/desired, etc.
}

public enum SkippedReason
{
    DropNotSupported = 1,            // exists only in current
    AlterNotSupported = 2,           // definition differs (needs alter)
    NotNullAddNotSupported = 3,      // v1 safety policy
    UnsupportedFeatureInDesired = 4, // future: detect in desired but prefer skipped over error (v1 is generally Error)
}
```

Note:
- Since v1 is “unsupported in desired -> immediate error”, `UnsupportedFeatureInDesired` is mainly for future versions

---

## 8. `SqlObjectRef` (target identification)

Even with fixed `dbo`, it is important to identify “what we are talking about” uniquely for logs and `-- Skipped:` output.

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class SqlObjectRef
{
    public string Schema { get; set; } = "dbo";
    public SqlObjectType Type { get; set; }
    public string Name { get; set; }      // table/index/constraint name

    // Additional information (e.g. columns or FK references)
    public string ParentName { get; set; } // e.g. the parent table name for a column
}

public enum SqlObjectType
{
    Table = 1,
    Column = 2,
    Constraint = 3,
    Index = 4,
    ForeignKey = 5,
}
```

Display convention examples:
- table: `dbo.Users`
- column: `dbo.Users.Email`
- index: `dbo.Users.IX_Users_Email` (or consistently `dbo.IX_Users_Email`)

---

## 9. Exceptions and Diagnostics (Failure Contracts)

### 9.1 Parse errors (`desired`)

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class DesiredSqlParseException : Exception
{
    public DesiredSqlParseException(string message, IReadOnlyList<SqlDiagnostic> diagnostics)
        : base(message) => Diagnostics = diagnostics;

    public IReadOnlyList<SqlDiagnostic> Diagnostics { get; }
}

public sealed class SqlDiagnostic
{
    public int? BatchIndex { get; set; }
    public string Message { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string Fragment { get; set; } // optional: a relevant snippet
}
```

### 9.2 v1 unsupported in `desired`

v1 contract:
- If `desired` contains unsupported statements: immediate error
- If supported statements include unsupported features (e.g. `CREATE INDEX ... WHERE`): immediate error
- Any schema other than `dbo`: immediate error
  - Message templates follow `dotnet-sqldef-scriptdom-visitor-spec.md`

```csharp
namespace SqlSchemaDef.Core.Planning;

public abstract class DesiredSqlException : Exception
{
    protected DesiredSqlException(string message, Exception inner = null) : base(message, inner) {}

    public int? BatchIndex { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

public sealed class UnsupportedDesiredStatementException : DesiredSqlException
{
    public UnsupportedDesiredStatementException(string message) : base(message) {}
    public string StatementType { get; set; }
}

public sealed class UnsupportedDesiredFeatureException : DesiredSqlException
{
    public UnsupportedDesiredFeatureException(string message) : base(message) {}
    public string StatementType { get; set; }
    public string FeatureName { get; set; }
}

public sealed class UnsupportedSchemaException : DesiredSqlException
{
    public UnsupportedSchemaException(string message) : base(message) {}
    public string SchemaName { get; set; }
}

public sealed class UnsupportedBatchSeparatorException : DesiredSqlException
{
    public UnsupportedBatchSeparatorException(string message) : base(message) {}
    public string SeparatorText { get; set; } // e.g. "GO 2"
}
```

### 9.3 Apply failures

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class ApplyFailedException : Exception
{
    public ApplyFailedException(string message, SqlOperation operation, Exception inner)
        : base(message, inner) => Operation = operation;

    public SqlOperation Operation { get; }
}
```

Contract:
- On apply failure, callers must be able to trace “which operation failed”

---

## 10. “Additive-only” Guarantee (API Perspective)

Provide aggregate information so callers do not mistakenly assume there were no alter/drop-equivalent diffs.

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class PlanSummary
{
    public int OperationCount { get; set; }
    public int SkippedCount { get; set; }

    // future: breakdown by kind
    public IReadOnlyDictionary<OperationKind, int> OperationCountByKind { get; set; }
    public IReadOnlyDictionary<SkippedReason, int> SkippedCountByReason { get; set; }
}
```

Either attach `Summary` to `MigrationPlan`, or provide `MigrationPlan.GetSummary()`.

---

## 11. Non-functional Requirements for v1 (API-related)

- Deterministic:
  - Always emit `Operations` and `Skipped` in deterministic order (sort keys + name order)
- Thread-safe:
  - Make `MigrationPlan` immutable and safe to read from multiple threads
- Cancellation:
  - `PlanAsync` / `ApplyAsync` accept and honor `CancellationToken` during DB access and loops
- Logging:
  - Allow implementations to accept `ILogger` (via options or ctor injection)
