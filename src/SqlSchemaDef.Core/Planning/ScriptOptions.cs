namespace SqlSchemaDef.Core.Planning
{
    public sealed class ScriptOptions
    {
        public ScriptHeaderMode HeaderMode { get; set; } = ScriptHeaderMode.DryRunStyle;
        public bool IncludeSkipped { get; set; } = true;
        public bool TerminateWithSemicolon { get; set; } = true;

        public string NewLine { get; set; } = "\n";
    }

    public enum ScriptHeaderMode
    {
        None = 0,
        DryRunStyle = 1,
    }
}

