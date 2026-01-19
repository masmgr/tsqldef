using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.SqlServer.Planning;

namespace SqlSchemaDef.Cli
{
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 2;
        private const int ExitDesiredParseError = 10;
        private const int ExitDesiredUnsupported = 11;
        private const int ExitApplyFailed = 20;

        private static int PrintUsage(int exitCode)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  SqlSchemaDef.Cli --connection <connectionString> --file <desired.sql> [--apply]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Notes:");
            Console.Error.WriteLine("  - Without --apply, prints the review script (dry-run).");
            Console.Error.WriteLine("  - v1 is additive-only (dbo fixed by default).");
            return exitCode;
        }

        private static string GetArg(IReadOnlyList<string> args, ref int i)
        {
            if (i + 1 >= args.Count)
            {
                throw new ArgumentException("Missing value for " + args[i]);
            }

            i++;
            return args[i];
        }

        public static async Task<int> Main(string[] args)
        {
            var argsList = args.ToList();

            string? connectionString = null;
            string? filePath = null;
            var apply = false;

            try
            {
                for (int i = 0; i < argsList.Count; i++)
                {
                    var a = argsList[i];
                    switch (a)
                    {
                        case "--connection":
                        case "-c":
                            connectionString = GetArg(argsList, ref i);
                            break;
                        case "--file":
                        case "-f":
                            filePath = GetArg(argsList, ref i);
                            break;
                        case "--apply":
                            apply = true;
                            break;
                        case "--help":
                        case "-h":
                            return PrintUsage(ExitOk);
                        default:
                            Console.Error.WriteLine("Unknown arg: " + a);
                            return PrintUsage(ExitUsage);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return PrintUsage(ExitUsage);
            }

            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(filePath))
            {
                return PrintUsage(ExitUsage);
            }

            var desiredSql = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

            var planner = new SqlServerSchemaPlanner();
            var applier = new SqlServerSchemaApplier();

            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync().ConfigureAwait(false);

                var plan = await planner.PlanAsync(conn, desiredSql, new PlannerOptions()).ConfigureAwait(false);

                Console.Write(plan.ToScript(new ScriptOptions { HeaderMode = ScriptHeaderMode.DryRunStyle }));

                if (apply && !plan.IsEmpty)
                {
                    await applier.ApplyAsync(conn, plan, new ApplyOptions()).ConfigureAwait(false);
                }

                return ExitOk;
            }
            catch (DesiredSqlParseException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitDesiredParseError;
            }
            catch (UnsupportedDesiredStatementException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitDesiredUnsupported;
            }
            catch (UnsupportedDesiredFeatureException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitDesiredUnsupported;
            }
            catch (UnsupportedSchemaException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitDesiredUnsupported;
            }
            catch (ApplyFailedException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitApplyFailed;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
