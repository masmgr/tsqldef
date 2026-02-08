using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using SqlSchemaDef.Core.Planning;
using SqlSchemaDef.Tests.Properties.Generators;

namespace SqlSchemaDef.Tests.Properties;

public sealed class MigrationPlanScriptPropertyTests
{
    [Property(MaxTest = 100)]
    public Property ToScript_IsDeterministic()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            var options = new ScriptOptions
            {
                HeaderMode = ScriptHeaderMode.None,
                NewLine = "\n",
            };
            var script1 = plan.ToScript(options);
            var script2 = plan.ToScript(options);
            return (script1 == script2)
                .Label("Scripts differ across calls");
        });
    }

    [Property(MaxTest = 100)]
    public Property ToScript_WithSemicolon_AllOperationsEndWithSemicolon()
    {
        return Prop.ForAll(Arb.From(DomainArbitraries.GenMigrationPlan()), plan =>
        {
            if (plan.Operations.Count == 0)
                return true.ToProperty();

            var options = new ScriptOptions
            {
                HeaderMode = ScriptHeaderMode.None,
                TerminateWithSemicolon = true,
                NewLine = "\n",
            };
            var script = plan.ToScript(options);
            var lines = script.Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.StartsWith("--", StringComparison.Ordinal))
                .ToArray();

            return lines.All(line => line.TrimEnd().EndsWith(';'))
                .Label($"Not all lines end with semicolon: [{string.Join(", ", lines.Take(3))}]");
        });
    }
}
