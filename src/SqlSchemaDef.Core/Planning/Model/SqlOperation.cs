namespace SqlSchemaDef.Core.Planning
{
    public sealed class SqlOperation
    {
        public string Description { get; set; }
        public string Sql { get; set; }
        public SqlObjectRef Target { get; set; }
        public OperationKind Kind { get; set; }
    }

    public enum OperationKind
    {
        CreateTable = 10,
        AddColumn = 20,
        AddConstraint = 30,
        CreateIndex = 40,
        AddForeignKey = 50,
        AddDescription = 60,
        UpdateDescription = 61,
    }
}
