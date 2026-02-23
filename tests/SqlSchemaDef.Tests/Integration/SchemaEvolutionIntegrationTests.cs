using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

[Trait("Category", "Integration")]
public sealed class SchemaEvolutionIntegrationTests
{
    [Fact]
    public async Task PlanApply_CreateTableWithConstraints_VerifyDbState()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        const string desiredSql = @"
CREATE TABLE dbo.Orders (
    Id int NOT NULL,
    Amount decimal(10,2) NOT NULL,
    Status nvarchar(20) NULL,
    CONSTRAINT PK_Orders PRIMARY KEY (Id),
    CONSTRAINT CK_Orders_Amount CHECK (Amount >= 0)
)
CREATE INDEX IX_Orders_Status ON dbo.Orders (Status)
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        // Verify actual DB state
        Assert.Equal(1, await CountTablesAsync(conn, "Orders"));
        Assert.Equal(1, await CountColumnsAsync(conn, "Orders", "Id"));
        Assert.Equal(1, await CountColumnsAsync(conn, "Orders", "Amount"));
        Assert.Equal(1, await CountColumnsAsync(conn, "Orders", "Status"));
        Assert.Equal(1, await CountConstraintsAsync(conn, "Orders", "PK_Orders"));
        Assert.Equal(1, await CountConstraintsAsync(conn, "Orders", "CK_Orders_Amount"));
        Assert.Equal(1, await CountIndexesAsync(conn, "Orders", "IX_Orders_Status"));

        // Idempotent
        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task PlanApply_AddColumnToExistingTable_VerifyDbState()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed existing table
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Users (Id int NOT NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        const string desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL)
ALTER TABLE dbo.Users ADD Email nvarchar(255) NULL
";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);
        Assert.Contains(plan1.Operations, op => op.Kind == OperationKind.AddColumn);

        await applier.ApplyAsync(conn2, plan1, new ApplyOptions());

        // Verify column exists
        Assert.Equal(1, await CountColumnsAsync(conn2, "Users", "Email"));

        // Idempotent
        var plan2 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task ExportPlanApply_RoundTrip_ColumnsAndIndexesConverge()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed table with index
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(100) NULL);
CREATE INDEX IX_Users_Name ON dbo.Users (Name);
";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        // Export current schema
        var export1 = await SqlServerSchemaExporter.ExportAsync(conn2, new ExportOptions());
        Assert.Contains("Id", export1.Script);
        Assert.Contains("Name", export1.Script);

        // Desired: same + new column + new index
        const string desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(100) NULL)
ALTER TABLE dbo.Users ADD Active bit NULL
CREATE INDEX IX_Users_Name ON dbo.Users (Name)
CREATE INDEX IX_Users_Active ON dbo.Users (Active)
";

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.False(plan.IsEmpty);
        Assert.Contains(plan.Operations, op => op.Kind == OperationKind.AddColumn);
        Assert.Contains(plan.Operations, op => op.Kind == OperationKind.CreateIndex);

        await applier.ApplyAsync(conn2, plan, new ApplyOptions());

        // Verify DB state
        Assert.Equal(1, await CountColumnsAsync(conn2, "Users", "Active"));
        Assert.Equal(1, await CountIndexesAsync(conn2, "Users", "IX_Users_Active"));

        // Re-export + plan = empty
        var export2 = await SqlServerSchemaExporter.ExportAsync(conn2, new ExportOptions());
        var plan2 = await planner.PlanAsync(conn2, export2.Script, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task SchemaEvolution_ThreeSteps_ConvergesCorrectly()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        // Step 1: Basic table with PK
        const string step1Sql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, CONSTRAINT PK_Users PRIMARY KEY (Id))
";
        var plan1 = await planner.PlanAsync(conn, step1Sql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);
        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        Assert.Equal(1, await CountTablesAsync(conn, "Users"));
        Assert.Equal(1, await CountConstraintsAsync(conn, "Users", "PK_Users"));

        var replan1 = await planner.PlanAsync(conn, step1Sql, new PlannerOptions());
        Assert.True(replan1.IsEmpty);

        // Step 2: Add Email column + unique index
        const string step2Sql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, CONSTRAINT PK_Users PRIMARY KEY (Id))
ALTER TABLE dbo.Users ADD Email nvarchar(255) NULL
CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email)
";
        var plan2 = await planner.PlanAsync(conn, step2Sql, new PlannerOptions());
        Assert.False(plan2.IsEmpty);
        await applier.ApplyAsync(conn, plan2, new ApplyOptions());

        Assert.Equal(1, await CountColumnsAsync(conn, "Users", "Email"));
        Assert.Equal(1, await CountIndexesAsync(conn, "Users", "IX_Users_Email"));

        var replan2 = await planner.PlanAsync(conn, step2Sql, new PlannerOptions());
        Assert.True(replan2.IsEmpty);

        // Step 3: Add Teams table + FK
        const string step3Sql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, CONSTRAINT PK_Users PRIMARY KEY (Id))
ALTER TABLE dbo.Users ADD Email nvarchar(255) NULL
ALTER TABLE dbo.Users ADD TeamId int NULL
CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email)
CREATE TABLE dbo.Teams (Id int NOT NULL, Name nvarchar(100) NOT NULL, CONSTRAINT PK_Teams PRIMARY KEY (Id))
ALTER TABLE dbo.Users ADD CONSTRAINT FK_Users_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams (Id)
";
        var plan3 = await planner.PlanAsync(conn, step3Sql, new PlannerOptions());
        Assert.False(plan3.IsEmpty);
        await applier.ApplyAsync(conn, plan3, new ApplyOptions());

        Assert.Equal(1, await CountTablesAsync(conn, "Teams"));
        Assert.Equal(1, await CountColumnsAsync(conn, "Users", "TeamId"));
        Assert.True(await ForeignKeyExistsAsync(conn, "FK_Users_Teams"));

        var replan3 = await planner.PlanAsync(conn, step3Sql, new PlannerOptions());
        Assert.True(replan3.IsEmpty);
    }

    [Fact]
    public async Task PlanApply_MultipleTablesWithForeignKeys_VerifyDbState()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        const string desiredSql = @"
CREATE TABLE dbo.Departments (
    Id int NOT NULL,
    Name nvarchar(100) NOT NULL,
    CONSTRAINT PK_Departments PRIMARY KEY (Id)
)
CREATE TABLE dbo.Employees (
    Id int NOT NULL,
    DeptId int NOT NULL,
    Name nvarchar(100) NOT NULL,
    CONSTRAINT PK_Employees PRIMARY KEY (Id)
)
CREATE TABLE dbo.Projects (
    Id int NOT NULL,
    LeadId int NOT NULL,
    Name nvarchar(200) NOT NULL,
    CONSTRAINT PK_Projects PRIMARY KEY (Id)
)
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Departments FOREIGN KEY (DeptId) REFERENCES dbo.Departments (Id)
ALTER TABLE dbo.Projects ADD CONSTRAINT FK_Projects_Employees FOREIGN KEY (LeadId) REFERENCES dbo.Employees (Id)
";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        // Verify tables
        Assert.Equal(1, await CountTablesAsync(conn, "Departments"));
        Assert.Equal(1, await CountTablesAsync(conn, "Employees"));
        Assert.Equal(1, await CountTablesAsync(conn, "Projects"));

        // Verify PKs
        Assert.Equal(1, await CountConstraintsAsync(conn, "Departments", "PK_Departments"));
        Assert.Equal(1, await CountConstraintsAsync(conn, "Employees", "PK_Employees"));
        Assert.Equal(1, await CountConstraintsAsync(conn, "Projects", "PK_Projects"));

        // Verify FKs
        Assert.True(await ForeignKeyExistsAsync(conn, "FK_Employees_Departments"));
        Assert.True(await ForeignKeyExistsAsync(conn, "FK_Projects_Employees"));

        // Verify FK ordering: CreateTable ops come before AddForeignKey ops
        var ops = plan1.Operations.ToArray();
        var lastCreateTableIndex = -1;
        var firstFkIndex = ops.Length;
        for (var i = 0; i < ops.Length; i++)
        {
            if (ops[i].Kind == OperationKind.CreateTable)
            {
                lastCreateTableIndex = i;
            }

            if (ops[i].Kind == OperationKind.AddForeignKey && i < firstFkIndex)
            {
                firstFkIndex = i;
            }
        }

        Assert.True(lastCreateTableIndex < firstFkIndex, "CreateTable operations should precede AddForeignKey operations");

        // Idempotent
        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task ExportThenPlan_WithConstraints_ProducesEmptyPlan()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed table with constraints
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Products (
    Id int NOT NULL,
    Name nvarchar(100) NOT NULL,
    Price decimal(10,2) NOT NULL,
    CONSTRAINT PK_Products PRIMARY KEY (Id),
    CONSTRAINT UQ_Products_Name UNIQUE (Name),
    CONSTRAINT CK_Products_Price CHECK (Price > 0)
);
CREATE INDEX IX_Products_Name ON dbo.Products (Name);
";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        // Export now includes constraints
        var export = await SqlServerSchemaExporter.ExportAsync(conn2, new ExportOptions());

        // Plan against export = truly empty (no operations, no skipped items)
        var planner = new SqlServerSchemaPlanner();
        var plan = await planner.PlanAsync(conn2, export.Script, new PlannerOptions());

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public async Task PlanApply_TableAndColumnDescriptions_VerifyDbState()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var desiredSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (Id int NOT NULL, Name nvarchar(100) NULL)",
            "GO",
            "EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'User accounts', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users'",
            "GO",
            "EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'Primary key', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'Id'",
        });

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);
        Assert.Contains(plan1.Operations, op => op.Kind == OperationKind.AddDescription);

        await applier.ApplyAsync(conn, plan1, new ApplyOptions());

        // Verify descriptions exist in DB
        Assert.Equal(1, await CountExtendedPropertiesAsync(conn, "Users", null!));
        Assert.Equal(1, await CountExtendedPropertiesAsync(conn, "Users", "Id"));

        // Idempotent
        var plan2 = await planner.PlanAsync(conn, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task PlanApply_DescriptionUpdate_GeneratesUpdate()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed with initial description
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (Id int NOT NULL);
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'Version 1', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users';
";
            await cmd.ExecuteNonQueryAsync();
        }

        var desiredSql = string.Join("\n", new[]
        {
            "CREATE TABLE dbo.Users (Id int NOT NULL)",
            "GO",
            "EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'Version 2', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users'",
        });

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.False(plan.IsEmpty);
        Assert.Contains(plan.Operations, op => op.Kind == OperationKind.UpdateDescription);

        await applier.ApplyAsync(conn2, plan, new ApplyOptions());

        // Idempotent after update
        var plan2 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    private static async Task<int> CountExtendedPropertiesAsync(SqlConnection conn, string tableName, string columnName)
    {
        await using var cmd = conn.CreateCommand();
        if (columnName == null)
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM sys.extended_properties ep
                JOIN sys.tables t ON ep.major_id = t.object_id
                WHERE t.name = @table AND ep.minor_id = 0 AND ep.name = 'MS_Description'
                AND t.schema_id = SCHEMA_ID(N'dbo')";
            cmd.Parameters.AddWithValue("@table", tableName);
        }
        else
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM sys.extended_properties ep
                JOIN sys.tables t ON ep.major_id = t.object_id
                JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = ep.minor_id
                WHERE t.name = @table AND c.name = @column AND ep.name = 'MS_Description'
                AND t.schema_id = SCHEMA_ID(N'dbo')";
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.Parameters.AddWithValue("@column", columnName);
        }

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountTablesAsync(SqlConnection conn, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @name AND schema_id = SCHEMA_ID(N'dbo')";
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountColumnsAsync(SqlConnection conn, string tableName, string columnName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.name = @table AND c.name = @column AND t.schema_id = SCHEMA_ID(N'dbo')";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@column", columnName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountIndexesAsync(SqlConnection conn, string tableName, string indexName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name = @table AND i.name = @index AND t.schema_id = SCHEMA_ID(N'dbo')";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@index", indexName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountConstraintsAsync(SqlConnection conn, string tableName, string constraintName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM sys.objects o
            JOIN sys.tables t ON o.parent_object_id = t.object_id
            WHERE t.name = @table AND o.name = @constraint AND t.schema_id = SCHEMA_ID(N'dbo')";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@constraint", constraintName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task ApplyWithSwap_ColumnTypeChange_RebuildsTableAndPreservesData()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed: Users with BIGINT Age column and data
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Age bigint NULL);
INSERT INTO dbo.Users (Id, Age) VALUES (1, 30);
";
            await cmd.ExecuteNonQueryAsync();
        }

        // Desired: Age changed to INT (narrowing = unsafe, requires rebuild)
        const string desiredSql = "CREATE TABLE dbo.Users (Id int NOT NULL, Age int NULL)";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions { EmitProposals = true });

        // No additive ops; alter is skipped; one proposal generated
        Assert.Empty(plan.Operations);
        Assert.Single(plan.Skipped);
        Assert.Single(plan.Proposals);

        await applier.ApplyAsync(conn2, plan, new ApplyOptions { ApplyProposals = true });

        // Verify column type changed to int
        var typeName = await GetColumnTypeNameAsync(conn2, "Users", "Age");
        Assert.Equal("int", typeName);

        // Verify data preserved
        var ageVal = await GetScalarAsync(conn2, "SELECT Age FROM dbo.Users WHERE Id = 1");
        Assert.Equal(30, Convert.ToInt32(ageVal, CultureInfo.InvariantCulture));

        // Verify shadow and old tables are gone
        Assert.Equal(0, await CountTablesAsync(conn2, "__Users_rebuild"));
        Assert.Equal(0, await CountTablesAsync(conn2, "Users_old"));
    }

    [Fact]
    public async Task ApplyWithSwap_ColumnTypeChangeAndConstraintAdd_SucceedsWithoutConstraintConflict()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (
    Id int NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Age bigint NULL
);
INSERT INTO dbo.Users (Id, Age) VALUES (1, 30);
";
            await cmd.ExecuteNonQueryAsync();
        }

        const string desiredSql = @"
CREATE TABLE dbo.Users (
    Id int NOT NULL,
    Age int NULL,
    CONSTRAINT PK_Users PRIMARY KEY (Id),
    CONSTRAINT CK_Users_Age CHECK (Age >= 0)
)";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions { EmitProposals = true });

        Assert.Single(plan.Proposals);
        Assert.Single(plan.Skipped);
        Assert.Empty(plan.Operations);

        await applier.ApplyAsync(conn2, plan, new ApplyOptions { ApplyProposals = true });

        Assert.Equal("int", await GetColumnTypeNameAsync(conn2, "Users", "Age"));
        Assert.Equal(1, await CountConstraintsAsync(conn2, "Users", "PK_Users"));
        Assert.Equal(1, await CountConstraintsAsync(conn2, "Users", "CK_Users_Age"));
        Assert.Equal(0, await CountTablesAsync(conn2, "__Users_rebuild"));
        Assert.Equal(0, await CountTablesAsync(conn2, "Users_old"));
    }

    [Fact]
    public async Task ApplyWithSwap_ProposalFailure_RollsBackOperationsInSingleTransaction()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        var proposal = new RebuildProposal
        {
            Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "T" },
            Description = "Failing proposal",
            Steps = new[]
            {
                new RebuildStep
                {
                    Kind = RebuildStepKind.CreateShadowTable,
                    Description = "create temporary shadow table",
                    Sql = "CREATE TABLE dbo.__T_rebuild (Id int NOT NULL)",
                },
                new RebuildStep
                {
                    Kind = RebuildStepKind.CopyData,
                    Description = "fail intentionally",
                    Sql = "THIS IS NOT VALID SQL",
                },
            },
        };

        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            new[]
            {
                new SqlOperation
                {
                    Kind = OperationKind.CreateTable,
                    Description = "Create table dbo.OperationAppliedBeforeProposal",
                    Sql = "CREATE TABLE dbo.OperationAppliedBeforeProposal (Id int NOT NULL)",
                    Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "OperationAppliedBeforeProposal" },
                },
            },
            Array.Empty<SkippedItem>(),
            new[] { proposal });

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var applier = new SqlServerSchemaApplier();
        await Assert.ThrowsAsync<RebuildFailedException>(() =>
            applier.ApplyAsync(conn, plan, new ApplyOptions { ApplyProposals = true }));

        Assert.Equal(0, await CountTablesAsync(conn, "OperationAppliedBeforeProposal"));
        Assert.Equal(0, await CountTablesAsync(conn, "__T_rebuild"));
    }

    [Fact]
    public async Task ApplyWithSwap_StepFails_ThrowsRebuildFailedException()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Build a plan with a deliberately bad SQL step
        var proposal = new RebuildProposal
        {
            Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "T" },
            Description = "Bad proposal",
            Steps = new[]
            {
                new RebuildStep { Kind = RebuildStepKind.CreateShadowTable, Description = "bad step", Sql = "THIS IS NOT VALID SQL AT ALL" },
            },
        };
        var plan = new MigrationPlan(
            new PlanMetadata { Schema = "dbo" },
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>(),
            new[] { proposal });

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var applier = new SqlServerSchemaApplier();
        var ex = await Assert.ThrowsAsync<RebuildFailedException>(() =>
            applier.ApplyAsync(conn, plan, new ApplyOptions { ApplyProposals = true }));

        Assert.NotNull(ex.Proposal);
        Assert.Equal("Bad proposal", ex.Proposal.Description);
        Assert.Equal(RebuildStepKind.CreateShadowTable, ex.Step.Kind);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task ApplyWithSwap_NoProposals_SucceedsWithoutError()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        const string desiredSql = "CREATE TABLE dbo.Items (Id int NOT NULL)";

        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan = await planner.PlanAsync(conn, desiredSql, new PlannerOptions { EmitProposals = true });
        Assert.False(plan.IsEmpty);
        Assert.Empty(plan.Proposals);

        // ApplyProposals=true with no proposals is a no-op
        await applier.ApplyAsync(conn, plan, new ApplyOptions { ApplyProposals = true });

        Assert.Equal(1, await CountTablesAsync(conn, "Items"));
    }

    private static async Task<string> GetColumnTypeNameAsync(SqlConnection conn, string tableName, string columnName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT TYPE_NAME(c.system_type_id) FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.name = @table AND c.name = @column AND t.schema_id = SCHEMA_ID(N'dbo')";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@column", columnName);
        var result = await cmd.ExecuteScalarAsync();
        return result as string ?? string.Empty;
    }

    private static async Task<object> GetScalarAsync(SqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result ?? DBNull.Value;
    }

    [Fact]
    public async Task PlanApply_NotNullColumnWithDefault_SucceedsAndIsIdempotent()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed existing table with data
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Users (Id int NOT NULL);
INSERT INTO dbo.Users (Id) VALUES (1), (2);
";
            await cmd.ExecuteNonQueryAsync();
        }

        const string desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Active bit DEFAULT (1) NOT NULL)
";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var applier = new SqlServerSchemaApplier();

        var plan1 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.False(plan1.IsEmpty);
        Assert.Contains(plan1.Operations, op => op.Kind == OperationKind.AddColumn);

        await applier.ApplyAsync(conn2, plan1, new ApplyOptions());

        // Verify column exists and existing rows got default value
        Assert.Equal(1, await CountColumnsAsync(conn2, "Users", "Active"));
        var activeVal = await GetScalarAsync(conn2, "SELECT Active FROM dbo.Users WHERE Id = 1");
        Assert.True(Convert.ToBoolean(activeVal, CultureInfo.InvariantCulture));

        // Idempotent
        var plan2 = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());
        Assert.True(plan2.IsEmpty);
    }

    [Fact]
    public async Task ExportThenPlan_PkClusteredRoundTrip_ProducesEmptyPlan()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed table with PK CLUSTERED (default) and UNIQUE NONCLUSTERED
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE dbo.Items (
    Id int NOT NULL,
    Code nvarchar(50) NOT NULL,
    CONSTRAINT PK_Items PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Items_Code UNIQUE NONCLUSTERED (Code)
);
";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        // Export includes CLUSTERED/NONCLUSTERED
        var export = await SqlServerSchemaExporter.ExportAsync(conn2, new ExportOptions());
        Assert.Contains("PRIMARY KEY CLUSTERED", export.Script);
        Assert.Contains("UNIQUE NONCLUSTERED", export.Script);

        // Plan against export = empty (no false diff)
        var planner = new SqlServerSchemaPlanner();
        var plan = await planner.PlanAsync(conn2, export.Script, new PlannerOptions());
        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public async Task Plan_StrictModeWithSkippedItems_ReturnsNonEmpty()
    {
        var master = SqlServerTestDatabase.GetMasterConnectionStringOrNull();
        if (string.IsNullOrWhiteSpace(master))
        {
            return;
        }

        await using var db = await SqlServerTestDatabase.CreateAsync(master);

        // Seed with a table
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Users (Id int NOT NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        // Desired: add NOT NULL column without DEFAULT → should be skipped
        const string desiredSql = @"
CREATE TABLE dbo.Users (Id int NOT NULL, Age int NOT NULL)
";

        await using var conn2 = new SqlConnection(db.ConnectionString);
        await conn2.OpenAsync();

        var planner = new SqlServerSchemaPlanner();
        var plan = await planner.PlanAsync(conn2, desiredSql, new PlannerOptions());

        Assert.NotEmpty(plan.Skipped);
        Assert.Contains(plan.Skipped, s => s.Reason == SkippedReason.NotNullAddNotSupported);
    }

    private static async Task<bool> ForeignKeyExistsAsync(SqlConnection conn, string fkName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", fkName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }
}
