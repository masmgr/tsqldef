# v1 公開API 詳細設計（SQL Server専用 / 追加のみ）

本書は `dotnet-sqldef-plan.md` の v1 方針（dbo固定、追加のみ、変更/削除は `-- Skipped:`、desired は許可DDL以外エラー）を前提に、.NET へ組み込みやすい **公開APIの具体的な型設計**をまとめる。

ターゲット:
- ライブラリは **.NET Standard 2.0** を対象とする
  - 公開APIのサンプルは `record` / `required` / `init` などの言語機能に依存しない形で記述する
  - 接続は **`Microsoft.Data.SqlClient.SqlConnection`** を利用する

---

## 1. 名前空間・アセンブリ構成（案）

- `SqlSchemaDef.Core`
  - `SqlSchemaDef.Core.Planning`（Plan/Operation/Options/Diagnostics）
  - `SqlSchemaDef.Core.Model`（内部モデル。公開は最小にする）
- `SqlSchemaDef.SqlServer`
  - `SqlSchemaDef.SqlServer.Planning`（SQL Server向け Planner 実装）

以降の型は、公開面を `SqlSchemaDef.Core.Planning` に寄せる想定。

---

## 2. 代表的な利用例（組み込み側）

```csharp
using Microsoft.Data.SqlClient;
using SqlSchemaDef.SqlServer.Planning;

await using var conn = new SqlConnection(connectionString);
await conn.OpenAsync(ct);

var planner = new SqlServerSchemaPlanner();
var plan = await planner.PlanAsync(conn, desiredSql, new PlannerOptions(), ct);

// レビュー用
var script = plan.ToScript();

// 適用（dry-run の場合は呼ばない）
if (!plan.IsEmpty)
{
    await plan.ApplyAsync(conn, new ApplyOptions(), ct);
}
```

---

## 3. 主要インターフェース

### 3.1 `ISchemaPlanner`

Planner は「current（DB）+ desired（SQL）」から `MigrationPlan` を生成する。

```csharp
namespace SqlSchemaDef.Core.Planning;

public interface ISchemaPlanner
{
    Task<MigrationPlan> PlanAsync(
        SqlConnection connection,
        string desiredSql,
        PlannerOptions options = null,
        CancellationToken cancellationToken = default);
}
```

SQL Server専用実装は `SqlSchemaDef.SqlServer.Planning.SqlServerSchemaPlanner : ISchemaPlanner`。

備考:
- `DbConnection` を受けて DI/テストを容易にする（実装は `SqlConnection` を要求してもよいが、公開面は `DbConnection` が扱いやすい）
- connection は呼び出し側が Open 済みを推奨。未 Open の場合の扱いは実装で明記する

---

## 4. PlannerOptions（v1: dbo固定/追加のみ）

v1の原則は「安全第一」。オプションも “誤って破壊的なDDLが出ない” 方向へ寄せる。

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class PlannerOptions
{
    // v1: dbo固定。将来拡張のために保持してもよいが、v1では変更不可として扱う。
    public string Schema { get; set; } = "dbo";

    // v1: desired SQL に許可ステートメント以外が混在したらエラー
    public UnsupportedDesiredStatementBehavior UnsupportedDesiredStatementBehavior { get; set; }
        = UnsupportedDesiredStatementBehavior.Error;

    // v1: 追加のみ。変更/削除に該当する差分は skipped として収集する。
    public PlanMode Mode { get; set; } = PlanMode.AdditiveOnly;

    // v1安全策: 既存行がある可能性のあるテーブルへの NOT NULL 列追加は skipped
    // （DEFAULT付きでの安全な追加などは v1.1 以降）
    public NotNullColumnAddBehavior NotNullColumnAddBehavior { get; set; }
        = NotNullColumnAddBehavior.Skip;

    // v1: currentの余計なオブジェクト/非対応差分はエラーにせず skipped
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
    // Error,  // 将来: 強制的に失敗させたい場合
}

public enum SurplusCurrentObjectBehavior
{
    CollectAsSkipped = 0,
    // SilentIgnore, // 将来: 何も出さない
    // Error,        // 将来: CIで厳格化
}
```

設計意図:
- v1は “やらないこと” を明確にするため `PlanMode.AdditiveOnly` を固定し、将来にのみ拡張
- `Schema` は v1固定だが、v2 以降を見据えてフィールドとして残してもよい

---

## 5. MigrationPlan（計画結果の表現）

### 5.1 形（immutable 推奨）

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class MigrationPlan
{
    public PlanMetadata Metadata { get; set; }

    // 実行すべきSQL（追加のみ）
    public IReadOnlyList<SqlOperation> Operations { get; set; }

    // 実行しないが通知すべき差分
    public IReadOnlyList<SkippedItem> Skipped { get; set; }

    public bool IsEmpty => Operations.Count == 0;

    // レビュー用途のスクリプト（GOは使わない。実行は ApplyAsync）
    public string ToScript(ScriptOptions options = null) => throw new NotImplementedException();

    // Applyはオプショナル。Coreではインターフェースのみ or 拡張メソッド化してもよい。
    public Task ApplyAsync(
        SqlConnection connection,
        ApplyOptions options = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public sealed class PlanMetadata
{
    public string Schema { get; set; }           // v1: dbo
    public DateTimeOffset PlannedAt { get; set; }
    public string PlannerVersion { get; set; }   // アセンブリ/パッケージ版など

    // 監査/表示用（任意）
    public string DatabaseName { get; set; }
    public string ServerVersion { get; set; }
}
```

備考:
- `ApplyAsync` を `SqlSchemaDef.SqlServer` 側の `SqlServerApplier` に分離してもよい
  - Core は plan の表現のみを提供し、DB依存は SqlServer パッケージに寄せる

### 5.2 ScriptOptions（ToScriptの出力制御）

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class ScriptOptions
{
    // 先頭ヘッダ（-- dry run -- / -- Apply -- など）
    public ScriptHeaderMode HeaderMode { get; set; } = ScriptHeaderMode.DryRunStyle;

    // `-- Skipped:` を出すか
    public bool IncludeSkipped { get; set; } = true;

    // SQL末尾に ; を付けるか（レビュー用）
    public bool TerminateWithSemicolon { get; set; } = true;
}

public enum ScriptHeaderMode
{
    None = 0,
    DryRunStyle = 1,
}

public sealed class ApplyOptions
{
    // v1: 例外時に直前のSQLを含めたメッセージを出すなど、実行器の挙動を調整する余地。
    public ApplyTransactionMode TransactionMode { get; set; } = ApplyTransactionMode.SingleTransaction;
}

public enum ApplyTransactionMode
{
    SingleTransaction = 0,
}
```

---

## 6. SqlOperation（実行単位）

v1は “1ステートメント = 1 operation” を原則にする（失敗時の追跡が容易）。

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class SqlOperation
{
    public string Description { get; set; } // 例: "Create table dbo.Users"
    public string Sql { get; set; }         // 例: "CREATE TABLE dbo.Users (...)"

    // ログ/エラー用の参照
    public SqlObjectRef Target { get; set; }

    // 適用順序のためのカテゴリ（ソートキー）
    public OperationKind Kind { get; set; }
}

public enum OperationKind
{
    CreateTable = 10,
    AddColumn = 20,
    AddConstraint = 30,
    CreateIndex = 40,
    AddForeignKey = 50,
}
```

---

## 7. SkippedItem（通知）

Skipped は “ユーザーが後で判断すべき差分” を失わないための仕組み。

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class SkippedItem
{
    public SkippedReason Reason { get; set; }
    public string Message { get; set; } // 例: "alter is not supported in v1: column dbo.Users.Email differs"
    public SqlObjectRef Target { get; set; }
    public string Details { get; set; }         // 任意: current/desiredの断片など
}

public enum SkippedReason
{
    DropNotSupported = 1,          // currentにのみ存在
    AlterNotSupported = 2,         // 定義差（変更が必要）
    NotNullAddNotSupported = 3,    // v1安全策
    UnsupportedFeatureInDesired = 4, // desiredで検出したが “エラーにせず” skipped にしたい場合（v1は基本 Error）
}
```

注:
- v1は “desiredに非対応が混ざると即エラー” を原則にするため、`UnsupportedFeatureInDesired` は将来用

---

## 8. SqlObjectRef（対象識別）

`dbo` 固定でも、ログや `-- Skipped:` の整形で “何の話か” を一意に表現できることが重要。

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class SqlObjectRef
{
    public string Schema { get; set; } = "dbo";
    public SqlObjectType Type { get; set; }
    public string Name { get; set; }      // table/index/constraint name

    // columnやFK参照など、追加情報が必要な場合
    public string ParentName { get; set; }        // 例: columnの親テーブル名
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

表示用の規約（例）:
- table: `dbo.Users`
- column: `dbo.Users.Email`
- index: `dbo.Users.IX_Users_Email`（または `dbo.IX_Users_Email` で統一）

---

## 9. 例外・診断（失敗時の契約）

### 9.1 パースエラー（desired）

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
    public string Message { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string Fragment { get; set; } // 任意: 該当テキスト断片
}
```

### 9.2 Apply失敗

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class ApplyFailedException : Exception
{
    public ApplyFailedException(string message, SqlOperation operation, Exception inner)
        : base(message, inner) => Operation = operation;

    public SqlOperation Operation { get; }
}
```

契約:
- Apply失敗時は “どの operation で失敗したか” を必ず辿れる

---

## 10. v1での追加のみ保証（API観点）

呼び出し側が「変更/削除が発生していない」と誤解しないよう、集約情報を提供する。

```csharp
namespace SqlSchemaDef.Core.Planning;

public sealed class PlanSummary
{
    public int OperationCount { get; set; }
    public int SkippedCount { get; set; }

    // 将来: kindsの内訳
    public IReadOnlyDictionary<OperationKind, int> OperationCountByKind { get; set; }
    public IReadOnlyDictionary<SkippedReason, int> SkippedCountByReason { get; set; }
}
```

`MigrationPlan` に `Summary` を持たせるか、`MigrationPlan.GetSummary()` を提供する。

---

## 11. v1の非機能要件（APIに関わるもの）

- Deterministic:
  - `Operations` と `Skipped` は常に決定的な順序（ソートキー + 名前順）で出力する
- Thread-safe:
  - `MigrationPlan` は immutable とし、複数スレッドから安全に参照可能にする
- Cancellation:
  - `PlanAsync` / `ApplyAsync` は `CancellationToken` を受け取り、DBアクセス/ループで尊重する
- Logging:
  - 実装パッケージ側で `ILogger` を受け取れるようにする（公開APIは options か ctor 注入）
