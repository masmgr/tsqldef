using System;
using SqlSchemaDef.Core.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class CoreOptionsAndExceptionsTests
{
    [Fact]
    public void PlannerOptions_Defaults_AreV1Safe()
    {
        var options = new PlannerOptions();
        Assert.Equal("dbo", options.Schema);
        Assert.Equal(PlanMode.AdditiveOnly, options.Mode);
        Assert.Equal(UnsupportedDesiredStatementBehavior.Error, options.UnsupportedDesiredStatementBehavior);
        Assert.Equal(NotNullColumnAddBehavior.Skip, options.NotNullColumnAddBehavior);
        Assert.Equal(SurplusCurrentObjectBehavior.CollectAsSkipped, options.SurplusCurrentObjectBehavior);
    }

    [Fact]
    public void ApplyOptions_Defaults_ToSingleTransaction()
    {
        var options = new ApplyOptions();
        Assert.Equal(ApplyTransactionMode.SingleTransaction, options.TransactionMode);
    }

    [Fact]
    public void ApplyOptions_DefaultApplyProposals_IsFalse()
    {
        var options = new ApplyOptions();
        Assert.False(options.ApplyProposals);
    }

    [Fact]
    public void RebuildFailedException_StoresProposalAndStep()
    {
        var proposal = new RebuildProposal { Description = "Rebuild T" };
        var step = new RebuildStep { Kind = RebuildStepKind.CopyData };
        var inner = new InvalidOperationException("inner");
        var ex = new RebuildFailedException("failed", proposal, step, inner);
        Assert.Same(proposal, ex.Proposal);
        Assert.Same(step, ex.Step);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal("failed", ex.Message);
    }

    [Fact]
    public void ApplyFailedException_ExposesOperationAndInnerException()
    {
        var operation = new SqlOperation
        {
            Kind = OperationKind.CreateTable,
            Description = "Create dbo.Users",
            Sql = "CREATE TABLE dbo.Users (Id int NOT NULL)",
            Target = new SqlObjectRef { Type = SqlObjectType.Table, Schema = "dbo", Name = "Users" },
        };

        var inner = new InvalidOperationException("boom");
        var ex = new ApplyFailedException("apply failed", operation, inner);
        Assert.Same(operation, ex.Operation);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void DesiredSqlException_LocationFields_AreSettable()
    {
        var ex = new UnsupportedDesiredStatementException("nope")
        {
            BatchIndex = 1,
            Line = 2,
            Column = 3,
            StatementType = "CreateViewStatement",
        };

        Assert.Equal(1, ex.BatchIndex);
        Assert.Equal(2, ex.Line);
        Assert.Equal(3, ex.Column);
        Assert.Equal("CreateViewStatement", ex.StatementType);
    }

    [Fact]
    public void PlanMetadata_DefaultSchema_IsDbo()
    {
        var metadata = new PlanMetadata();
        Assert.Equal("dbo", metadata.Schema);
    }
}
