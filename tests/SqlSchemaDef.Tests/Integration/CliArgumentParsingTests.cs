using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SqlSchemaDef.Core.Planning;
using Xunit;

namespace SqlSchemaDef.Tests;

public sealed class CliArgumentParsingTests
{
    [Fact]
    public async Task NoArgs_ReturnsUsageExitCode()
    {
        var result = await RunCliAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StdErr);
    }

    [Fact]
    public async Task UnknownCommand_ReturnsUsageExitCode()
    {
        var result = await RunCliAsync("nope");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown command:", result.StdErr);
    }

    [Fact]
    public async Task Plan_UnsupportedFormat_ReturnsUsageExitCode()
    {
        var result = await RunCliAsync(
            "plan",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--file", "desired.sql",
            "--format", "xml");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unsupported format", result.StdErr);
    }

    [Fact]
    public async Task Apply_PlanFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "apply",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--plan", "nonexistent_plan.json");

        Assert.DoesNotContain("Unknown arg: --plan", result.StdErr);
        Assert.DoesNotContain("not supported", result.StdErr);
        Assert.NotEqual(2, result.ExitCode);
    }

    [Fact]
    public async Task Apply_FileAndPlanAreMutuallyExclusive()
    {
        var result = await RunCliAsync(
            "apply",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--file", "desired.sql",
            "--plan", "plan.json");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("mutually exclusive", result.StdErr);
    }

    [Fact]
    public async Task MissingArgValue_ReturnsNonZeroAndPrintsException()
    {
        var result = await RunCliAsync("plan", "--file");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Missing value", result.StdErr);
    }

    [Fact]
    public async Task Plan_StrictFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "plan",
            "--strict",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;");

        Assert.DoesNotContain("Unknown arg: --strict", result.StdErr);
    }

    [Fact]
    public async Task Plan_IncludeFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "plan",
            "--include", "Users,Teams",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;");

        Assert.DoesNotContain("Unknown arg: --include", result.StdErr);
    }

    [Fact]
    public async Task Plan_ExcludeFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "plan",
            "--exclude", "Logs",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;");

        Assert.DoesNotContain("Unknown arg: --exclude", result.StdErr);
    }

    [Fact]
    public async Task Plan_EmitSwapSqlFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "plan",
            "--emit-swap-sql",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;");

        Assert.DoesNotContain("Unknown arg: --emit-swap-sql", result.StdErr);
    }

    [Fact]
    public async Task Help_MentionsEmitSwapSql()
    {
        var result = await RunCliAsync("--help");
        Assert.Contains("--emit-swap-sql", result.StdErr);
    }

    [Fact]
    public async Task Apply_SwapFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "apply",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--swap",
            "--file", "nonexistent.sql");

        Assert.DoesNotContain("Unknown arg: --swap", result.StdErr);
        Assert.NotEqual(2, result.ExitCode);
    }

    [Fact]
    public async Task Help_MentionsSwapFlag()
    {
        var result = await RunCliAsync("--help");
        Assert.Contains("--swap", result.StdErr);
    }

    [Fact]
    public void RebuildFailedException_IsMappedToApplyFailedExitCode()
    {
        var proposal = new RebuildProposal { Description = "Rebuild T" };
        var step = new RebuildStep { Kind = RebuildStepKind.CopyData };
        var ex = new RebuildFailedException("rebuild step failed", proposal, step, new InvalidOperationException("inner"));

        var result = InvokeHandleException(ex);

        Assert.Equal(20, result.ExitCode);
        Assert.Contains("rebuild step failed", result.StdErr);
    }

    [Fact]
    public void UnsupportedBatchSeparator_IsMappedToUnsupportedExitCode()
    {
        var result = InvokeHandleException(new UnsupportedBatchSeparatorException("bad separator"));

        Assert.Equal(11, result.ExitCode);
        Assert.Contains("bad separator", result.StdErr);
    }

    [Fact]
    public async Task Export_SchemaFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "export",
            "--schema", "sales");

        Assert.DoesNotContain("Unknown arg: --schema", result.StdErr);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task Plan_SchemaFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "plan",
            "--schema", "sales",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;");

        Assert.DoesNotContain("Unknown arg: --schema", result.StdErr);
    }

    [Fact]
    public async Task Apply_SchemaFlag_IsRecognized()
    {
        var result = await RunCliAsync(
            "apply",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--schema", "sales",
            "--file", "nonexistent.sql");

        Assert.DoesNotContain("Unknown arg: --schema", result.StdErr);
        Assert.NotEqual(2, result.ExitCode);
    }

    [Fact]
    public void UnhandledException_PrintsMessageOnly()
    {
        var result = InvokeHandleException(new InvalidOperationException("something went wrong"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("something went wrong", result.StdErr);
        Assert.DoesNotContain("at SqlSchemaDef", result.StdErr);
    }

    [Fact]
    public async Task Help_MentionsSchemaOption()
    {
        var result = await RunCliAsync("--help");
        Assert.Contains("--schema", result.StdErr);
    }

    [Fact]
    public async Task Apply_PlanWithScopeFilters_WarnsToStderr()
    {
        var result = await RunCliAsync(
            "apply",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--plan", "nonexistent_plan.json",
            "--include", "Users");

        Assert.Contains("--include/--exclude are ignored", result.StdErr);
    }

    [Fact]
    public async Task Plan_NonexistentFile_PrintsFileNotFound()
    {
        var result = await RunCliAsync(
            "plan",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--file", "nonexistent_file_that_does_not_exist.sql");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("File not found", result.StdErr);
    }

    [Fact]
    public async Task Apply_NonexistentPlanFile_PrintsFileNotFound()
    {
        var result = await RunCliAsync(
            "apply",
            "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            "--plan", "nonexistent_plan_that_does_not_exist.json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("File not found", result.StdErr);
    }

    [Fact]
    public void ParseCsvArg_TrimsWhitespaceAndRemovesEmptyEntries()
    {
        var values = SqlSchemaDef.Cli.CliArgumentParser.ParseCsvArg(" Users, Orders ,, Logs ");
        Assert.Equal(new[] { "Users", "Orders", "Logs" }, values.ToArray());
    }

    [Fact]
    public void ParseCsvArg_WhitespaceOnly_ReturnsEmpty()
    {
        var values = SqlSchemaDef.Cli.CliArgumentParser.ParseCsvArg("   ");
        Assert.Empty(values);
    }

    private static async Task<CliTestHelper.CliRunResult> RunCliAsync(params string[] args)
    {
        return await CliTestHelper.RunAsync(args);
    }

    private static CliTestHelper.CliRunResult InvokeHandleException(Exception ex)
    {
        var handle = typeof(SqlSchemaDef.Cli.Program).GetMethod(
            "HandleException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(handle);

        return CliTestHelper.Capture(() =>
        {
            var rawResult = handle!.Invoke(null, new object[] { ex });
            return Assert.IsType<int>(rawResult);
        });
    }
}
