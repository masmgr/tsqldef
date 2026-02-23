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
        private const int ExitStrictViolation = 30;

        private static int PrintUsage(int exitCode)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  SqlSchemaDef.Cli export --connection <cs> [--out <desired.sql>] [--schema <schema>]");
            Console.Error.WriteLine("  SqlSchemaDef.Cli plan   --connection <cs> --file <desired.sql> [--schema <schema>] [--format script|json] [--strict] [--emit-swap-sql] [--include <tables>] [--exclude <tables>]");
            Console.Error.WriteLine("  SqlSchemaDef.Cli apply  --connection <cs> (--file <desired.sql> | --plan <plan.json>) [--schema <schema>] [--include <tables>] [--exclude <tables>] [--swap]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Notes:");
            Console.Error.WriteLine("  - plan prints the review script (dry-run) or JSON plan.");
            Console.Error.WriteLine("  - --strict exits non-zero (30) if any skipped items exist.");
            Console.Error.WriteLine("  - --emit-swap-sql generates rebuild proposals for non-additive diffs.");
            Console.Error.WriteLine("  - --swap executes rebuild proposals for non-additive column changes (shadow-table swap).");
            Console.Error.WriteLine("  - --schema defaults to 'dbo' when omitted.");
            Console.Error.WriteLine("  - v1 is additive-only.");
            return exitCode;
        }

        private static string GetArg(List<string> args, ref int i)
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
            try
            {
                return await RunAsync(args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return PrintUsage(ExitUsage);
            }

            if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
            {
                return PrintUsage(ExitOk);
            }

            var command = args[0];
            var argsList = args.Skip(1).ToList();

            switch (command)
            {
                case "export":
                    return await RunExportAsync(argsList).ConfigureAwait(false);
                case "plan":
                    return await RunPlanAsync(argsList).ConfigureAwait(false);
                case "apply":
                    return await RunApplyAsync(argsList).ConfigureAwait(false);
                case "--help":
                case "-h":
                    return PrintUsage(ExitOk);
                default:
                    Console.Error.WriteLine("Unknown command: " + command);
                    return PrintUsage(ExitUsage);
            }
        }

        private static async Task<int> RunExportAsync(List<string> args)
        {
            string? connectionString = null;
            string? outPath = null;
            string? schema = null;

            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--connection":
                    case "-c":
                        connectionString = GetArg(args, ref i);
                        break;
                    case "--out":
                    case "-o":
                        outPath = GetArg(args, ref i);
                        break;
                    case "--schema":
                    case "-s":
                        schema = GetArg(args, ref i);
                        break;
                    case "--help":
                    case "-h":
                        return PrintUsage(ExitOk);
                    default:
                        Console.Error.WriteLine("Unknown arg: " + a);
                        return PrintUsage(ExitUsage);
                }
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return PrintUsage(ExitUsage);
            }

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            var exportOptions = new ExportOptions();
            if (schema != null)
            {
                exportOptions.Schema = schema;
            }

            var result = await SqlServerSchemaExporter.ExportAsync(conn, exportOptions).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(outPath))
            {
                Console.Write(result.Script);
            }
            else
            {
                await File.WriteAllTextAsync(outPath, result.Script).ConfigureAwait(false);
            }

            return ExitOk;
        }

        private static async Task<int> RunPlanAsync(List<string> args)
        {
            string? connectionString = null;
            string? filePath = null;
            string? schema = null;
            var format = "script";
            var strict = false;
            var emitSwapSql = false;
            string? includeArg = null;
            string? excludeArg = null;

            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--connection":
                    case "-c":
                        connectionString = GetArg(args, ref i);
                        break;
                    case "--file":
                    case "-f":
                        filePath = GetArg(args, ref i);
                        break;
                    case "--schema":
                    case "-s":
                        schema = GetArg(args, ref i);
                        break;
                    case "--format":
                        format = GetArg(args, ref i);
                        break;
                    case "--strict":
                        strict = true;
                        break;
                    case "--emit-swap-sql":
                        emitSwapSql = true;
                        break;
                    case "--include":
                        includeArg = GetArg(args, ref i);
                        break;
                    case "--exclude":
                        excludeArg = GetArg(args, ref i);
                        break;
                    case "--help":
                    case "-h":
                        return PrintUsage(ExitOk);
                    default:
                        Console.Error.WriteLine("Unknown arg: " + a);
                        return PrintUsage(ExitUsage);
                }
            }

            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(filePath))
            {
                return PrintUsage(ExitUsage);
            }

            var normalizedFormat = (format ?? "script").Trim().ToLowerInvariant();
            if (normalizedFormat != "script" && normalizedFormat != "json")
            {
                Console.Error.WriteLine("Unsupported format: " + format);
                return PrintUsage(ExitUsage);
            }

            var desiredSql = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

            var plannerOptions = new PlannerOptions { EmitProposals = emitSwapSql };
            if (schema != null)
            {
                plannerOptions.Schema = schema;
            }

            if (includeArg != null)
            {
                plannerOptions.IncludeTablePatterns = CliArgumentParser.ParseCsvArg(includeArg);
            }
            if (excludeArg != null)
            {
                plannerOptions.ExcludeTablePatterns = CliArgumentParser.ParseCsvArg(excludeArg);
            }

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            var planner = new SqlServerSchemaPlanner();
            var plan = await planner.PlanAsync(conn, desiredSql, plannerOptions).ConfigureAwait(false);

            if (normalizedFormat == "json")
            {
                Console.Write(MigrationPlanSerializer.ToJson(plan));
            }
            else
            {
                Console.Write(plan.ToScript(new ScriptOptions
                {
                    HeaderMode = ScriptHeaderMode.DryRunStyle,
                    IncludeProposals = emitSwapSql,
                }));
            }

            if (strict && plan.Skipped.Count > 0)
            {
                Console.Error.WriteLine("Strict mode: " + plan.Skipped.Count + " skipped item(s) detected.");
                return ExitStrictViolation;
            }

            return ExitOk;
        }

        private static async Task<int> RunApplyAsync(List<string> args)
        {
            string? connectionString = null;
            string? filePath = null;
            string? planJsonPath = null;
            string? schema = null;
            string? includeArg = null;
            string? excludeArg = null;
            var applySwap = false;

            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--connection":
                    case "-c":
                        connectionString = GetArg(args, ref i);
                        break;
                    case "--file":
                    case "-f":
                        filePath = GetArg(args, ref i);
                        break;
                    case "--plan":
                        planJsonPath = GetArg(args, ref i);
                        break;
                    case "--schema":
                    case "-s":
                        schema = GetArg(args, ref i);
                        break;
                    case "--include":
                        includeArg = GetArg(args, ref i);
                        break;
                    case "--exclude":
                        excludeArg = GetArg(args, ref i);
                        break;
                    case "--swap":
                        applySwap = true;
                        break;
                    case "--help":
                    case "-h":
                        return PrintUsage(ExitOk);
                    default:
                        Console.Error.WriteLine("Unknown arg: " + a);
                        return PrintUsage(ExitUsage);
                }
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return PrintUsage(ExitUsage);
            }

            if (!string.IsNullOrWhiteSpace(filePath) && !string.IsNullOrWhiteSpace(planJsonPath))
            {
                Console.Error.WriteLine("--file and --plan are mutually exclusive.");
                return PrintUsage(ExitUsage);
            }

            if (string.IsNullOrWhiteSpace(filePath) && string.IsNullOrWhiteSpace(planJsonPath))
            {
                return PrintUsage(ExitUsage);
            }

            MigrationPlan plan;

            if (!string.IsNullOrWhiteSpace(planJsonPath))
            {
                var json = await File.ReadAllTextAsync(planJsonPath).ConfigureAwait(false);
                plan = MigrationPlanSerializer.FromJson(json);
            }
            else
            {
                var desiredSql = await File.ReadAllTextAsync(filePath!).ConfigureAwait(false);

                var plannerOptions = new PlannerOptions { EmitProposals = applySwap };
                if (schema != null)
                {
                    plannerOptions.Schema = schema;
                }

                if (includeArg != null)
                {
                    plannerOptions.IncludeTablePatterns = CliArgumentParser.ParseCsvArg(includeArg);
                }
                if (excludeArg != null)
                {
                    plannerOptions.ExcludeTablePatterns = CliArgumentParser.ParseCsvArg(excludeArg);
                }

                await using var planConn = new SqlConnection(connectionString);
                await planConn.OpenAsync().ConfigureAwait(false);

                var planner = new SqlServerSchemaPlanner();
                plan = await planner.PlanAsync(planConn, desiredSql, plannerOptions).ConfigureAwait(false);
            }

            PlanValidator.ValidateForApply(plan);

            if (applySwap)
            {
                PrintRebuildWarnings(plan);
            }

            Console.Write(plan.ToScript(new ScriptOptions
            {
                HeaderMode = ScriptHeaderMode.DryRunStyle,
                IncludeProposals = applySwap,
                ProposalsWillBeApplied = applySwap,
            }));

            if (!plan.IsEmpty || (applySwap && plan.Proposals.Count > 0))
            {
                await using var applyConn = new SqlConnection(connectionString);
                await applyConn.OpenAsync().ConfigureAwait(false);

                var applier = new SqlServerSchemaApplier();
                await applier.ApplyAsync(applyConn, plan, new ApplyOptions { ApplyProposals = applySwap }).ConfigureAwait(false);
            }

            return ExitOk;
        }

        private static void PrintRebuildWarnings(MigrationPlan plan)
        {
            foreach (var proposal in plan.Proposals)
            {
                if (!string.IsNullOrEmpty(proposal.Warning))
                {
                    Console.Error.WriteLine("WARNING (rebuild " + (proposal.Target?.ToDisplayName() ?? string.Empty) + "): " + proposal.Warning);
                }
            }
        }

        private static int HandleException(Exception ex)
        {
            switch (ex)
            {
                case DesiredSqlParseException parse:
                    Console.Error.WriteLine(parse.Message);
                    return ExitDesiredParseError;
                case UnsupportedDesiredStatementException statement:
                    Console.Error.WriteLine(statement.Message);
                    return ExitDesiredUnsupported;
                case UnsupportedDesiredFeatureException feature:
                    Console.Error.WriteLine(feature.Message);
                    return ExitDesiredUnsupported;
                case UnsupportedSchemaException schema:
                    Console.Error.WriteLine(schema.Message);
                    return ExitDesiredUnsupported;
                case UnsupportedBatchSeparatorException separator:
                    Console.Error.WriteLine(separator.Message);
                    return ExitDesiredUnsupported;
                case RebuildFailedException rebuildFailed:
                    Console.Error.WriteLine(rebuildFailed.Message);
                    return ExitApplyFailed;
                case ApplyFailedException applyFailed:
                    Console.Error.WriteLine(applyFailed.Message);
                    return ExitApplyFailed;
                default:
                    Console.Error.WriteLine(ex.ToString());
                    return 1;
            }
        }
    }
}
