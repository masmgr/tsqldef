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
        RecreateConstraint = 25,
        AddConstraint = 30,
        RecreateIndex = 35,
        CreateIndex = 40,
        AddForeignKey = 50,
        RecreateForeignKey = 55,
        AddDescription = 60,
        UpdateDescription = 61,
        DropDescription = 70,
        DropForeignKey = 71,
        DropIndex = 72,
        DropConstraint = 73,
        DropColumn = 74,
    }
}
