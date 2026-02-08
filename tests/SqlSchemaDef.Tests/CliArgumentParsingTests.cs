using System;
using System.IO;
using System.Threading.Tasks;
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
                "--format", "json",
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
    public async Task Apply_PlanJsonNotSupported_ReturnsUsageExitCode()
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
                "--plan", "plan.json",
            });

            Assert.Equal(2, exitCode);
            Assert.Contains("not supported", stderr.ToString());
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
}
