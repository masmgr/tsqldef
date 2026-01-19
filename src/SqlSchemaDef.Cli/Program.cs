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
        private static int PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  SqlSchemaDef.Cli --connection <connectionString> --file <desired.sql> [--apply]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Notes:");
            Console.Error.WriteLine("  - Without --apply, prints the review script (dry-run).");
            Console.Error.WriteLine("  - v1 is additive-only (dbo fixed by default).");
            return 2;
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
                            return PrintUsage();
                        default:
                            Console.Error.WriteLine("Unknown arg: " + a);
                            return PrintUsage();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return PrintUsage();
            }

            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(filePath))
            {
                return PrintUsage();
            }

            var desiredSql = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            var planner = new SqlServerSchemaPlanner();
            var applier = new SqlServerSchemaApplier();

            var plan = await planner.PlanAsync(conn, desiredSql, new PlannerOptions()).ConfigureAwait(false);

            Console.Write(plan.ToScript(new ScriptOptions { HeaderMode = ScriptHeaderMode.DryRunStyle }));

            if (apply && !plan.IsEmpty)
            {
                await applier.ApplyAsync(conn, plan, new ApplyOptions()).ConfigureAwait(false);
            }

            return 0;
        }
    }
}
