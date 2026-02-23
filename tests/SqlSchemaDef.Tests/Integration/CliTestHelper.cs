using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlSchemaDef.Tests;

internal static class CliTestHelper
{
    private static readonly SemaphoreSlim ConsoleLock = new SemaphoreSlim(1, 1);

    internal readonly struct CliRunResult
    {
        public CliRunResult(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut = stdOut;
            StdErr = stdErr;
        }

        public int ExitCode { get; }

        public string StdOut { get; }

        public string StdErr { get; }
    }

    internal sealed class TempSqlFile : IDisposable
    {
        public TempSqlFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    public static async Task<CliRunResult> RunAsync(params string[] args)
    {
        return await CaptureAsync(() => SqlSchemaDef.Cli.Program.Main(args)).ConfigureAwait(false);
    }

    public static async Task<CliRunResult> CaptureAsync(Func<Task<int>> action)
    {
        await ConsoleLock.WaitAsync().ConfigureAwait(false);
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = await action().ConfigureAwait(false);
            return new CliRunResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            ConsoleLock.Release();
        }
    }

    public static CliRunResult Capture(Func<int> action)
    {
        ConsoleLock.Wait();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = action();
            return new CliRunResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            ConsoleLock.Release();
        }
    }

    public static async Task<TempSqlFile> CreateTempSqlFileAsync(string sql)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "SqlSchemaDef_" + Guid.NewGuid().ToString("N") + ".sql");

        await File.WriteAllTextAsync(path, sql, Encoding.UTF8).ConfigureAwait(false);
        return new TempSqlFile(path);
    }
}
