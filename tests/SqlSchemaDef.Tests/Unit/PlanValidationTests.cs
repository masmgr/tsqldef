using System;
using SqlSchemaDef.Core.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class PlanValidationTests
{
    [Fact]
    public void ValidateForApply_WithOnlyAdditiveOps_DoesNotThrow()
    {
        var plan = new MigrationPlan(
            new PlanMetadata(),
            new[]
            {
                new SqlOperation { Kind = OperationKind.CreateTable, Sql = "CREATE TABLE dbo.T (Id int NOT NULL)" },
                new SqlOperation { Kind = OperationKind.AddColumn, Sql = "ALTER TABLE dbo.T ADD Col int NULL" },
                new SqlOperation { Kind = OperationKind.AddConstraint, Sql = "ALTER TABLE dbo.T ADD CONSTRAINT PK PRIMARY KEY (Id)" },
                new SqlOperation { Kind = OperationKind.CreateIndex, Sql = "CREATE INDEX IX ON dbo.T (Col)" },
                new SqlOperation { Kind = OperationKind.AddForeignKey, Sql = "ALTER TABLE dbo.T ADD CONSTRAINT FK FOREIGN KEY (Col) REFERENCES dbo.R (Id)" },
            },
            Array.Empty<SkippedItem>());

        PlanValidator.ValidateForApply(plan);
    }

    [Fact]
    public void ValidateForApply_EmptyPlan_DoesNotThrow()
    {
        var plan = new MigrationPlan(
            new PlanMetadata(),
            Array.Empty<SqlOperation>(),
            Array.Empty<SkippedItem>());

        PlanValidator.ValidateForApply(plan);
    }

    [Fact]
    public void ValidateForApply_WithUnknownOperationKind_ThrowsInvalidOperationException()
    {
        var plan = new MigrationPlan(
            new PlanMetadata(),
            new[]
            {
                new SqlOperation { Kind = OperationKind.CreateTable, Sql = "CREATE TABLE dbo.T (Id int NOT NULL)" },
                new SqlOperation { Kind = (OperationKind)999, Sql = "DROP TABLE dbo.T" },
            },
            Array.Empty<SkippedItem>());

        var ex = Assert.Throws<InvalidOperationException>(() => PlanValidator.ValidateForApply(plan));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void ValidateForApply_NullPlan_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PlanValidator.ValidateForApply(null));
    }
}
