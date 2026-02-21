using System;
using System.Linq;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class ScopeFilterTests
{
    [Fact]
    public void PlannerOptions_Defaults_HaveNullFilters()
    {
        var options = new PlannerOptions();
        Assert.Null(options.IncludeTablePatterns);
        Assert.Null(options.ExcludeTablePatterns);
    }

    [Fact]
    public void IsMatch_ExactMatch_IsCaseInsensitive()
    {
        Assert.True(TableNameMatcher.IsMatch("Users", "users"));
        Assert.True(TableNameMatcher.IsMatch("Users", "Users"));
        Assert.True(TableNameMatcher.IsMatch("USERS", "users"));
    }

    [Fact]
    public void IsMatch_ExactMatch_DoesNotMatchDifferentName()
    {
        Assert.False(TableNameMatcher.IsMatch("Teams", "Users"));
    }

    [Fact]
    public void IsMatch_WildcardSuffix_MatchesPrefix()
    {
        Assert.True(TableNameMatcher.IsMatch("Users", "User*"));
        Assert.True(TableNameMatcher.IsMatch("UserProfiles", "User*"));
        Assert.False(TableNameMatcher.IsMatch("Teams", "User*"));
    }

    [Fact]
    public void IsMatch_EmptyOrNullPattern_MatchesAnything()
    {
        Assert.True(TableNameMatcher.IsMatch("Anything", null));
        Assert.True(TableNameMatcher.IsMatch("Anything", string.Empty));
    }

    [Theory]
    [InlineData("Users", true)]
    [InlineData("Teams", true)]
    [InlineData("Orders", true)]
    public void ShouldInclude_NullFilters_IncludesAll(string tableName, bool expected)
    {
        Assert.Equal(expected, TableNameMatcher.ShouldInclude(tableName, null, null));
    }

    [Theory]
    [InlineData("Users", true)]
    [InlineData("Teams", true)]
    [InlineData("Orders", false)]
    public void ShouldInclude_WithIncludeOnly_FiltersCorrectly(string tableName, bool expected)
    {
        var include = new[] { "Users", "Teams" };
        Assert.Equal(expected, TableNameMatcher.ShouldInclude(tableName, include, null));
    }

    [Theory]
    [InlineData("Users", false)]
    [InlineData("Teams", true)]
    [InlineData("Orders", true)]
    public void ShouldInclude_WithExcludeOnly_FiltersCorrectly(string tableName, bool expected)
    {
        var exclude = new[] { "Users" };
        Assert.Equal(expected, TableNameMatcher.ShouldInclude(tableName, null, exclude));
    }

    [Theory]
    [InlineData("Users", true)]
    [InlineData("UserProfiles", true)]
    [InlineData("UsersArchive", false)]
    [InlineData("Teams", false)]
    public void ShouldInclude_WithIncludeAndExclude_CombinesCorrectly(string tableName, bool expected)
    {
        var include = new[] { "User*" };
        var exclude = new[] { "UsersArchive" };
        Assert.Equal(expected, TableNameMatcher.ShouldInclude(tableName, include, exclude));
    }

    [Fact]
    public void ShouldInclude_EmptyInclude_TreatedAsAll()
    {
        Assert.True(TableNameMatcher.ShouldInclude("Anything", Array.Empty<string>(), null));
    }

    [Fact]
    public void ShouldInclude_EmptyExclude_TreatedAsNone()
    {
        Assert.True(TableNameMatcher.ShouldInclude("Anything", null, Array.Empty<string>()));
    }

    [Fact]
    public void Diff_WithIncludeFilter_OnlyIncludedTablesGetOperations()
    {
        var desired = new DatabaseModel();
        var usersTable = desired.GetOrAddTable("dbo", "Users");
        usersTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var teamsTable = desired.GetOrAddTable("dbo", "Teams");
        teamsTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { IncludeTablePatterns = new[] { "Users" } };

        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Single(plan.Operations);
        Assert.Equal("dbo.Users", plan.Operations[0].Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WithExcludeFilter_ExcludedTablesAreOmitted()
    {
        var desired = new DatabaseModel();
        var usersTable = desired.GetOrAddTable("dbo", "Users");
        usersTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var teamsTable = desired.GetOrAddTable("dbo", "Teams");
        teamsTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { ExcludeTablePatterns = new[] { "Teams" } };

        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Single(plan.Operations);
        Assert.Equal("dbo.Users", plan.Operations[0].Target.ToDisplayName());
    }

    [Fact]
    public void Diff_WithExcludeFilter_CurrentOnlyExcludedTablesAreNotSkipped()
    {
        var desired = new DatabaseModel();
        var usersTable = desired.GetOrAddTable("dbo", "Users");
        usersTable.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var currentUsers = current.GetOrAddTable("dbo", "Users");
        currentUsers.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        var currentExcluded = current.GetOrAddTable("dbo", "Excluded");
        currentExcluded.Columns["ID"] = new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { ExcludeTablePatterns = new[] { "Excluded" } };

        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Empty(plan.Operations);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Diff_WithWildcardInclude_MatchesByPrefix()
    {
        var desired = new DatabaseModel();
        desired.GetOrAddTable("dbo", "Users").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desired.GetOrAddTable("dbo", "UserProfiles").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };
        desired.GetOrAddTable("dbo", "Teams").Columns["ID"] =
            new ColumnModel { Name = "Id", SqlType = "int", IsNullable = false };

        var current = new DatabaseModel();
        var metadata = new PlanMetadata { Schema = "dbo" };
        var options = new PlannerOptions { IncludeTablePatterns = new[] { "User*" } };

        var plan = SchemaDiffer.Diff(current, desired, metadata, options);

        Assert.Equal(2, plan.Operations.Count);
        var tableNames = plan.Operations.Select(op => op.Target.Name).Order().ToArray();
        Assert.Equal("UserProfiles", tableNames[0]);
        Assert.Equal("Users", tableNames[1]);
    }
}
