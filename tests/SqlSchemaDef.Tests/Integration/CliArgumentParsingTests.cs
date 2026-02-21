using System;
using System.IO;
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
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(Array.Empty<string>());
            Assert.Equal(2, exitCode);
            Assert.Contains("Usage:", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task UnknownCommand_ReturnsUsageExitCode()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[] { "nope" });
            Assert.Equal(2, exitCode);
            Assert.Contains("Unknown command:", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Plan_UnsupportedFormat_ReturnsUsageExitCode()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "plan",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
                "--file", "desired.sql",
                "--format", "xml",
            });

            Assert.Equal(2, exitCode);
            Assert.Contains("Unsupported format", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Apply_PlanFlag_IsRecognized()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            // --plan is now recognized (not "Unknown arg" or "not supported").
            // Will fail because file doesn't exist, but that's a runtime error, not a usage error.
            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "apply",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
                "--plan", "nonexistent_plan.json",
            });

            var output = stderr.ToString();
            Assert.DoesNotContain("Unknown arg: --plan", output);
            Assert.DoesNotContain("not supported", output);
            Assert.NotEqual(2, exitCode);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Apply_FileAndPlanAreMutuallyExclusive()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "apply",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
                "--file", "desired.sql",
                "--plan", "plan.json",
            });

            Assert.Equal(2, exitCode);
            Assert.Contains("mutually exclusive", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task MissingArgValue_ReturnsNonZeroAndPrintsException()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[] { "plan", "--file" });
            Assert.Equal(1, exitCode);
            Assert.Contains("Missing value", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Plan_StrictFlag_IsRecognized()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            // --strict should be recognized (not "Unknown arg").
            // It will fail later because --file is missing, but that's expected.
            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "plan",
                "--strict",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            });

            var output = stderr.ToString();
            Assert.DoesNotContain("Unknown arg: --strict", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Plan_IncludeFlag_IsRecognized()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "plan",
                "--include", "Users,Teams",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            });

            var output = stderr.ToString();
            Assert.DoesNotContain("Unknown arg: --include", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Plan_ExcludeFlag_IsRecognized()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "plan",
                "--exclude", "Logs",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            });

            var output = stderr.ToString();
            Assert.DoesNotContain("Unknown arg: --exclude", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Plan_EmitSwapSqlFlag_IsRecognized()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var exitCode = await SqlSchemaDef.Cli.Program.Main(new[]
            {
                "plan",
                "--emit-swap-sql",
                "--connection", "Server=(local);Database=master;Trusted_Connection=True;",
            });

            var output = stderr.ToString();
            Assert.DoesNotContain("Unknown arg: --emit-swap-sql", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Help_MentionsEmitSwapSql()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            await SqlSchemaDef.Cli.Program.Main(new[] { "--help" });

            var output = stderr.ToString();
            Assert.Contains("--emit-swap-sql", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void UnsupportedBatchSeparator_IsMappedToUnsupportedExitCode()
    {
        var originalError = Console.Error;
        try
        {
            using var stderr = new StringWriter();
            Console.SetError(stderr);

            var handle = typeof(SqlSchemaDef.Cli.Program).GetMethod(
                "HandleException",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(handle);
            var result = handle!.Invoke(null, new object[] { new UnsupportedBatchSeparatorException("bad separator") });

            var exitCode = Assert.IsType<int>(result);
            Assert.Equal(11, exitCode);
            Assert.Contains("bad separator", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
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
}
